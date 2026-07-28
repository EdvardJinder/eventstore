using EventStoreCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;
using PostgresExtensions = EventStoreCore.Postgres.ModelBuilderExtensions;
using SqliteExtensions = EventStoreCore.Sqlite.ModelBuilderExtensions;
using SqlServerExtensions = EventStoreCore.SqlServer.ModelBuilderExtensions;

namespace EventStoreCore.Tests;

public sealed class RelationalMigrationUpgradeTests :
    IClassFixture<PostgresFixture>,
    IClassFixture<SqlServerFixture>
{
    private const int StreamCount = 24;
    private const int EventsPerStream = 8;
    private const int OutboxMessageCount = 32;

    private readonly PostgresFixture _postgresFixture;
    private readonly SqlServerFixture _sqlServerFixture;

    public RelationalMigrationUpgradeTests(
        PostgresFixture postgresFixture,
        SqlServerFixture sqlServerFixture)
    {
        _postgresFixture = postgresFixture;
        _sqlServerFixture = sqlServerFixture;
    }

    public static IEnumerable<object[]> ExistingProviders =>
    [
        [MigrationProvider.Postgres],
        [MigrationProvider.SqlServer]
    ];

    [Theory]
    [MemberData(nameof(ExistingProviders))]
    public async Task Generated_migration_upgrades_populated_previous_schema(
        MigrationProvider provider)
    {
        await VerifyUpgradeAsync(
            provider switch
            {
                MigrationProvider.Postgres =>
                    options => options.UseNpgsql(_postgresFixture.ConnectionString),
                MigrationProvider.SqlServer =>
                    options => options.UseSqlServer(_sqlServerFixture.ConnectionString),
                _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null)
            },
            provider switch
            {
                MigrationProvider.Postgres =>
                    options => new PreviousPostgresContext(options),
                MigrationProvider.SqlServer =>
                    options => new PreviousSqlServerContext(options),
                _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null)
            },
            provider switch
            {
                MigrationProvider.Postgres =>
                    options => new CurrentPostgresContext(options),
                MigrationProvider.SqlServer =>
                    options => new CurrentSqlServerContext(options),
                _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null)
            });
    }

    [Fact]
    public async Task Generated_initial_migration_creates_working_sqlite_schema()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"eventstore-migration-{Guid.NewGuid():N}.db");
        try
        {
            var optionsBuilder = new DbContextOptionsBuilder();
            optionsBuilder.UseSqlite(
                $"Data Source={databasePath};Pooling=False");
            var cancellationToken = TestContext.Current.CancellationToken;

            await using var current = new CurrentSqliteContext(optionsBuilder.Options);
            var databaseCreator = current.GetService<IRelationalDatabaseCreator>();
            await databaseCreator.CreateAsync(cancellationToken);
            var currentModel = current.GetService<IDesignTimeModel>().Model;
            await ApplyGeneratedMigrationAsync(
                current,
                sourceModel: null,
                currentModel,
                cancellationToken);

            await current.Database.OpenConnectionAsync(cancellationToken);
            await using (var command = current.Database.GetDbConnection().CreateCommand())
            {
                command.CommandText =
                    "SELECT pk FROM pragma_table_info('Events') WHERE name = 'Sequence'";
                Assert.Equal(
                    1L,
                    Convert.ToInt64(
                        await command.ExecuteScalarAsync(cancellationToken)));
            }
            await current.Database.CloseConnectionAsync();

            SeedData(current);
            await current.SaveChangesAsync(cancellationToken);
            var eventHead = await current.Set<DbEvent>()
                .MaxAsync(@event => @event.Sequence, cancellationToken);
            var outboxHead = await current.Set<DbOutboxMessage>()
                .MaxAsync(message => message.Sequence, cancellationToken);
            current.ChangeTracker.Clear();

            await VerifyPopulatedSchemaAsync(
                current,
                eventHead,
                outboxHead,
                cancellationToken);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    private static async Task VerifyUpgradeAsync(
        Action<DbContextOptionsBuilder> configureProvider,
        Func<DbContextOptions, MigrationContractContext> createPreviousContext,
        Func<DbContextOptions, MigrationContractContext> createCurrentContext)
    {
        var optionsBuilder = new DbContextOptionsBuilder();
        configureProvider(optionsBuilder);
        var cancellationToken = TestContext.Current.CancellationToken;

        IModel previousModel;
        long previousEventHead;
        long previousOutboxHead;
        await using (var previous = createPreviousContext(optionsBuilder.Options))
        {
            var databaseCreator = previous.GetService<IRelationalDatabaseCreator>();
            if (await databaseCreator.ExistsAsync(cancellationToken))
            {
                await databaseCreator.DeleteAsync(cancellationToken);
            }

            await databaseCreator.CreateAsync(cancellationToken);
            previousModel = previous.GetService<IDesignTimeModel>().Model;
            await ApplyGeneratedMigrationAsync(
                previous,
                sourceModel: null,
                previousModel,
                cancellationToken);

            SeedData(previous);
            await previous.SaveChangesAsync(cancellationToken);

            previousEventHead = await previous.Set<DbEvent>()
                .MaxAsync(@event => @event.Sequence, cancellationToken);
            previousOutboxHead = await previous.Set<DbOutboxMessage>()
                .MaxAsync(message => message.Sequence, cancellationToken);
        }

        await using var current = createCurrentContext(optionsBuilder.Options);
        var currentModel = current.GetService<IDesignTimeModel>().Model;
        var operations = await ApplyGeneratedMigrationAsync(
            current,
            previousModel,
            currentModel,
            cancellationToken);

        AssertUpgradeOperations(operations);
        await VerifyPopulatedSchemaAsync(
            current,
            previousEventHead,
            previousOutboxHead,
            cancellationToken);
    }

    private static async Task VerifyPopulatedSchemaAsync(
        MigrationContractContext current,
        long previousEventHead,
        long previousOutboxHead,
        CancellationToken cancellationToken)
    {
        Assert.Equal(
            StreamCount,
            await current.Set<DbStream>().CountAsync(cancellationToken));
        Assert.Equal(
            StreamCount * EventsPerStream,
            await current.Set<DbEvent>().CountAsync(cancellationToken));
        Assert.Equal(
            OutboxMessageCount,
            await current.Set<DbOutboxMessage>().CountAsync(cancellationToken));
        Assert.Equal(
            StreamCount * EventsPerStream,
            await current.Set<DbEvent>()
                .Select(@event => @event.Sequence)
                .Distinct()
                .CountAsync(cancellationToken));

        current.Streams.StartStream(
            "migration-verification",
            Guid.NewGuid(),
            events: [new MigrationEvent("after-upgrade")]);
        var newOutboxMessage = CreateOutboxMessage();
        current.Set<DbOutboxMessage>().Add(newOutboxMessage);
        await current.SaveChangesAsync(cancellationToken);

        var newEventSequence = await current.Set<DbEvent>()
            .MaxAsync(@event => @event.Sequence, cancellationToken);
        Assert.True(newEventSequence > previousEventHead);
        Assert.True(newOutboxMessage.Sequence > previousOutboxHead);

        var existingEvent = await current.Set<DbEvent>()
            .AsNoTracking()
            .OrderBy(@event => @event.Sequence)
            .FirstAsync(cancellationToken);
        current.Set<DbEvent>().Add(
            new DbEvent
            {
                EventId = Guid.NewGuid(),
                StreamId = existingEvent.StreamId,
                StreamType = existingEvent.StreamType,
                TenantId = existingEvent.TenantId,
                Version = existingEvent.Version,
                Type = existingEvent.Type,
                TypeName = existingEvent.TypeName,
                Data = existingEvent.Data,
                Timestamp = DateTimeOffset.UtcNow,
                Headers = "{}",
                SchemaVersion = 1
            });
        await Assert.ThrowsAsync<DbUpdateException>(
            () => current.SaveChangesAsync(cancellationToken));
    }

    private static async Task<IReadOnlyList<MigrationOperation>> ApplyGeneratedMigrationAsync(
        DbContext context,
        IModel? sourceModel,
        IModel targetModel,
        CancellationToken cancellationToken)
    {
        var modelDiffer = context.GetService<IMigrationsModelDiffer>();
        var operations = modelDiffer.GetDifferences(
            sourceModel?.GetRelationalModel(),
            targetModel.GetRelationalModel());
        var commands = context.GetService<IMigrationsSqlGenerator>()
            .Generate(operations, targetModel);
        await context.GetService<IMigrationCommandExecutor>()
            .ExecuteNonQueryAsync(
                commands,
                context.GetService<IRelationalConnection>(),
                cancellationToken);
        return operations;
    }

    private static void AssertUpgradeOperations(
        IReadOnlyList<MigrationOperation> operations)
    {
        Assert.Contains(
            operations.OfType<DropPrimaryKeyOperation>(),
            operation => operation.Table == "Events");
        Assert.Contains(
            operations.OfType<DropIndexOperation>(),
            operation =>
                operation.Table == "Events" &&
                operation.Name == "IX_Events_Sequence");
        Assert.Contains(
            operations.OfType<AddPrimaryKeyOperation>(),
            operation =>
                operation.Table == "Events" &&
                operation.Columns.SequenceEqual([nameof(DbEvent.Sequence)]));
        Assert.Contains(
            operations.OfType<CreateIndexOperation>(),
            operation =>
                operation.Table == "Events" &&
                operation.IsUnique &&
                operation.Columns.SequenceEqual(
                [
                    nameof(DbEvent.StreamId),
                    nameof(DbEvent.StreamType),
                    nameof(DbEvent.TenantId),
                    nameof(DbEvent.Version)
                ]));
    }

    private static void SeedData(MigrationContractContext context)
    {
        for (var streamIndex = 0; streamIndex < StreamCount; streamIndex++)
        {
            var events = Enumerable.Range(0, EventsPerStream)
                .Select(eventIndex => (object)new MigrationEvent(
                    $"{streamIndex}:{eventIndex}"))
                .ToArray();
            context.Streams.StartStream(
                $"migration-{streamIndex % 3}",
                Guid.NewGuid(),
                new Guid(streamIndex % 4, 0, 0, new byte[8]),
                events);
        }

        context.Set<DbOutboxMessage>().AddRange(
            Enumerable.Range(0, OutboxMessageCount)
                .Select(_ => CreateOutboxMessage()));
    }

    private static DbOutboxMessage CreateOutboxMessage() =>
        new()
        {
            EventId = Guid.NewGuid(),
            Type = typeof(MigrationEvent).AssemblyQualifiedName!,
            TypeName = nameof(MigrationEvent),
            Data = "{}",
            Timestamp = DateTimeOffset.UtcNow,
            SourceEntityType = typeof(object).AssemblyQualifiedName!,
            SourceEntityKey = "{}"
        };

    public enum MigrationProvider
    {
        Postgres,
        SqlServer
    }

    private sealed record MigrationEvent(string Value);

    private abstract class MigrationContractContext(DbContextOptions options)
        : DbContext(options)
    {
        protected static void ConfigurePreviousEventKey(ModelBuilder modelBuilder)
        {
            var eventBuilder = modelBuilder.Entity<DbEvent>();
            var entityType = eventBuilder.Metadata;
            var compositeProperties = new[]
            {
                entityType.FindProperty(nameof(DbEvent.StreamId))!,
                entityType.FindProperty(nameof(DbEvent.StreamType))!,
                entityType.FindProperty(nameof(DbEvent.TenantId))!,
                entityType.FindProperty(nameof(DbEvent.Version))!
            };
            var compositeIndex = entityType.FindIndex(compositeProperties);
            if (compositeIndex is not null)
            {
                entityType.RemoveIndex(compositeIndex);
            }

            eventBuilder.HasKey(
                @event => new
                {
                    @event.StreamId,
                    @event.StreamType,
                    @event.TenantId,
                    @event.Version
                });
            eventBuilder.HasIndex(@event => @event.Sequence)
                .IsUnique();
        }
    }

    private sealed class PreviousPostgresContext(DbContextOptions options)
        : MigrationContractContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            PostgresExtensions.UseEventStore(modelBuilder);
            PostgresExtensions.UseEntityOutbox(modelBuilder);
            ConfigurePreviousEventKey(modelBuilder);
        }
    }

    private sealed class CurrentPostgresContext(DbContextOptions options)
        : MigrationContractContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            PostgresExtensions.UseEventStore(modelBuilder);
            PostgresExtensions.UseEntityOutbox(modelBuilder);
        }
    }

    private sealed class PreviousSqlServerContext(DbContextOptions options)
        : MigrationContractContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            SqlServerExtensions.UseEventStore(modelBuilder);
            SqlServerExtensions.UseEntityOutbox(modelBuilder);
            ConfigurePreviousEventKey(modelBuilder);
        }
    }

    private sealed class CurrentSqlServerContext(DbContextOptions options)
        : MigrationContractContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            SqlServerExtensions.UseEventStore(modelBuilder);
            SqlServerExtensions.UseEntityOutbox(modelBuilder);
        }
    }

    private sealed class CurrentSqliteContext(DbContextOptions options)
        : MigrationContractContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            SqliteExtensions.UseEventStore(modelBuilder);
            SqliteExtensions.UseEntityOutbox(modelBuilder);
        }
    }
}

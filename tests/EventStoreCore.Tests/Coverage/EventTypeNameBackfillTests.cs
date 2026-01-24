using EventStoreCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EventStoreCore.Tests;

public class EventTypeNameBackfillTests
{
    private sealed class BackfillDbContext : DbContext
    {
        public BackfillDbContext(DbContextOptions<BackfillDbContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            ModelBuilderExtensions.ConfigureEventStoreModel(modelBuilder);
        }
    }

    private sealed class CustomNamedEvent
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class DefaultNamedEvent
    {
        public string Name { get; set; } = string.Empty;
    }

    [Fact]
    public async Task BackfillEventTypeNamesAsync_UsesRegistryWhenAvailable()
    {
        var services = new ServiceCollection();
        services.AddDbContext<BackfillDbContext>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString("N")));
        services.AddEventStore();
        services.AddEventStore(builder => builder.AddEvent<CustomNamedEvent>("custom_event"));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BackfillDbContext>();

        var streamId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        db.Set<DbStream>().Add(new DbStream
        {
            Id = streamId,
            TenantId = tenantId,
            CurrentVersion = 1,
            CreatedTimestamp = DateTimeOffset.UtcNow,
            UpdatedTimestamp = DateTimeOffset.UtcNow
        });

        db.Set<DbEvent>().Add(new DbEvent
        {
            TenantId = tenantId,
            StreamId = streamId,
            Version = 1,
            Sequence = 1,
            Type = typeof(CustomNamedEvent).AssemblyQualifiedName!,
            TypeName = string.Empty,
            Data = System.Text.Json.JsonSerializer.Serialize(new CustomNamedEvent()),
            Timestamp = DateTimeOffset.UtcNow,
            EventId = Guid.NewGuid()
        });

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var updated = await db.BackfillEventTypeNamesAsync(ct: TestContext.Current.CancellationToken);

        var dbEvent = await db.Set<DbEvent>()
            .AsNoTracking()
            .FirstAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, updated);
        Assert.Equal("custom_event", dbEvent.TypeName);
    }

    [Fact]
    public async Task BackfillEventTypeNamesAsync_FallsBackToAqnParsing()
    {
        var services = new ServiceCollection();
        services.AddDbContext<BackfillDbContext>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString("N")));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BackfillDbContext>();

        var streamId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        db.Set<DbStream>().Add(new DbStream
        {
            Id = streamId,
            TenantId = tenantId,
            CurrentVersion = 1,
            CreatedTimestamp = DateTimeOffset.UtcNow,
            UpdatedTimestamp = DateTimeOffset.UtcNow
        });

        db.Set<DbEvent>().Add(new DbEvent
        {
            TenantId = tenantId,
            StreamId = streamId,
            Version = 1,
            Sequence = 1,
            Type = typeof(DefaultNamedEvent).AssemblyQualifiedName!,
            TypeName = string.Empty,
            Data = System.Text.Json.JsonSerializer.Serialize(new DefaultNamedEvent()),
            Timestamp = DateTimeOffset.UtcNow,
            EventId = Guid.NewGuid()
        });

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var updated = await db.BackfillEventTypeNamesAsync(ct: TestContext.Current.CancellationToken);

        var dbEvent = await db.Set<DbEvent>()
            .AsNoTracking()
            .FirstAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, updated);
        Assert.Equal("default_named_event", dbEvent.TypeName);
    }
}

using EventStoreCore;
using EventStoreCore.Abstractions;
using EventStoreCore.Persistence.EntityFrameworkCore;
using EventStoreCore.Persistence.EntityFrameworkCore.Postgres;
using Medallion.Threading.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static EventStoreCore.Tests.EventStoreFixture;

namespace EventStoreCore.Tests;

/// <summary>
/// Integration tests for the ProjectionManager implementation.
/// Tests the full lifecycle of projection management operations against a real PostgreSQL database.
/// </summary>
public class ProjectionManagerTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    #region Test Types

    public class TestEvent
    {
        public string Value { get; set; } = string.Empty;
    }

    public class TestSnapshot
    {
        public Guid Id { get; set; }
        public string Value { get; set; } = string.Empty;
    }

    public class TestProjection : IProjection<TestSnapshot>
    {
        public static Task Evolve(TestSnapshot snapshot, IEvent @event, IProjectionContext context, CancellationToken ct)
        {
            if (@event is IEvent<TestEvent> e)
            {
                snapshot.Id = e.StreamId;
                snapshot.Value = e.Data.Value;
            }
            return Task.CompletedTask;
        }

        public static Task ClearAsync(IProjectionContext context, CancellationToken ct)
        {
            return context.DbContext.Set<TestSnapshot>().ExecuteDeleteAsync(ct);
        }
    }

    public class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.UseEventStore();
            modelBuilder.Entity<TestSnapshot>(e =>
            {
                e.HasKey(x => x.Id);
            });
        }
    }

    #endregion

    private ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddDbContext<TestDbContext>(options =>
            options.UseNpgsql(fixture.ConnectionString));

        services.AddEventStore(c =>
        {
            c.ExistingDbContext<TestDbContext>();
            c.AddProjection<TestDbContext, TestProjection, TestSnapshot>(ProjectionMode.Eventual, p =>
            {
                p.Handles<TestEvent>();
            });
            c.AddProjectionDaemon<TestDbContext>(_ => new PostgresDistributedSynchronizationProvider(fixture.ConnectionString));
        });

        services.AddLogging();
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Creates a projection status record directly in the database (bypasses RebuildAsync).
    /// </summary>
    private async Task<DbProjectionStatus> CreateProjectionStatusAsync(
        TestDbContext db,
        string projectionName,
        ProjectionState state = ProjectionState.Active,
        long position = 0,
        string? lastError = null,
        long? failedEventSequence = null)
    {
        var status = new DbProjectionStatus
        {
            ProjectionName = projectionName,
            Version = 1,
            State = state,
            Position = position,
            LastError = lastError,
            FailedEventSequence = failedEventSequence
        };
        db.Set<DbProjectionStatus>().Add(status);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return status;
    }

    [Fact]
    public async Task GetStatusAsync_ReturnsNull_WhenProjectionNotRegistered()
    {
        // Arrange
        var provider = BuildServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        var manager = scope.ServiceProvider.GetRequiredService<IProjectionManager>();

        // Act
        var status = await manager.GetStatusAsync("NonExistent.Projection", TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(status);
    }

    [Fact]
    public async Task GetStatusAsync_ReturnsDefaultStatus_WhenProjectionRegisteredButNotInitialized()
    {
        // Arrange
        var provider = BuildServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        
        // Clean up any existing records
        await db.Set<DbProjectionStatus>().ExecuteDeleteAsync(TestContext.Current.CancellationToken);

        var manager = scope.ServiceProvider.GetRequiredService<IProjectionManager>();
        var projectionName = typeof(TestProjection).FullName!;

        // Act
        var status = await manager.GetStatusAsync(projectionName, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(status);
        Assert.Equal(projectionName, status.ProjectionName);
        Assert.Equal(ProjectionState.Active, status.State);
        Assert.Equal(0, status.Position);
    }

    [Fact]
    public async Task GetAllStatusesAsync_ReturnsEmptyList_WhenNoProjectionsAndNoRecords()
    {
        // Arrange - use a minimal setup without projections
        var services = new ServiceCollection();
        services.AddDbContext<EventStoreDbContext>(options =>
            options.UseNpgsql(fixture.ConnectionString));
        services.AddEventStore(c => c.ExistingDbContext<EventStoreDbContext>());
        services.AddLogging();
        
        // Manually register a minimal ProjectionManager for testing (no projections registered)
        services.AddScoped<IProjectionManager>(sp =>
        {
            var db = sp.GetRequiredService<EventStoreDbContext>();
            var lockProvider = new PostgresDistributedSynchronizationProvider(fixture.ConnectionString);
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ProjectionManager<EventStoreDbContext>>>();
            // Use reflection to create instance since constructor is internal
            var type = typeof(ProjectionManager<>).MakeGenericType(typeof(EventStoreDbContext));
            var ctor = type.GetConstructors(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)[0];
            return (IProjectionManager)ctor.Invoke(new object[] { db, lockProvider, Enumerable.Empty<ProjectionRegistration>(), logger });
        });

        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EventStoreDbContext>();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        
        // Clean up any existing records
        await db.Set<DbProjectionStatus>().ExecuteDeleteAsync(TestContext.Current.CancellationToken);

        var manager = scope.ServiceProvider.GetRequiredService<IProjectionManager>();

        // Act
        var statuses = await manager.GetAllStatusesAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(statuses);
        Assert.Empty(statuses);
    }

    [Fact]
    public async Task GetAllStatusesAsync_ReturnsRegisteredProjections()
    {
        // Arrange
        var provider = BuildServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        var manager = scope.ServiceProvider.GetRequiredService<IProjectionManager>();

        // Act
        var statuses = await manager.GetAllStatusesAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(statuses);
        Assert.Contains(statuses, s => s.ProjectionName == typeof(TestProjection).FullName);
    }

    [Fact]
    public async Task RebuildAsync_ThrowsInvalidOperationException_WhenProjectionNotRegistered()
    {
        // Arrange
        var provider = BuildServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        var manager = scope.ServiceProvider.GetRequiredService<IProjectionManager>();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.RebuildAsync("NonExistent.Projection", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PauseAsync_ThrowsInvalidOperationException_WhenProjectionNotFound()
    {
        // Arrange
        var provider = BuildServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        var manager = scope.ServiceProvider.GetRequiredService<IProjectionManager>();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.PauseAsync("NonExistent.Projection", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PauseAsync_SetsStateToPaused()
    {
        // Arrange
        var provider = BuildServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        var projectionName = typeof(TestProjection).FullName!;
        
        // Clean up and create an active projection status
        await db.Set<DbProjectionStatus>().Where(s => s.ProjectionName == projectionName)
            .ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        await CreateProjectionStatusAsync(db, projectionName, ProjectionState.Active);

        var manager = scope.ServiceProvider.GetRequiredService<IProjectionManager>();

        // Act
        await manager.PauseAsync(projectionName, TestContext.Current.CancellationToken);

        // Assert
        var status = await manager.GetStatusAsync(projectionName, TestContext.Current.CancellationToken);
        Assert.NotNull(status);
        Assert.Equal(ProjectionState.Paused, status.State);
    }

    [Fact]
    public async Task PauseAsync_ThrowsInvalidOperationException_WhenProjectionIsFaulted()
    {
        // Arrange
        var provider = BuildServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        var projectionName = typeof(TestProjection).FullName!;
        
        // Clean up and create a faulted projection status
        await db.Set<DbProjectionStatus>().Where(s => s.ProjectionName == projectionName)
            .ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        await CreateProjectionStatusAsync(db, projectionName, ProjectionState.Faulted, lastError: "Test error");

        var manager = scope.ServiceProvider.GetRequiredService<IProjectionManager>();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.PauseAsync(projectionName, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ResumeAsync_ThrowsInvalidOperationException_WhenProjectionNotPaused()
    {
        // Arrange
        var provider = BuildServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        var projectionName = typeof(TestProjection).FullName!;
        
        // Clean up and create an active projection status
        await db.Set<DbProjectionStatus>().Where(s => s.ProjectionName == projectionName)
            .ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        await CreateProjectionStatusAsync(db, projectionName, ProjectionState.Active);

        var manager = scope.ServiceProvider.GetRequiredService<IProjectionManager>();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.ResumeAsync(projectionName, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ResumeAsync_SetsStateToActive()
    {
        // Arrange
        var provider = BuildServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        var projectionName = typeof(TestProjection).FullName!;
        
        // Clean up and create a paused projection status
        await db.Set<DbProjectionStatus>().Where(s => s.ProjectionName == projectionName)
            .ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        await CreateProjectionStatusAsync(db, projectionName, ProjectionState.Paused);

        var manager = scope.ServiceProvider.GetRequiredService<IProjectionManager>();

        // Act
        await manager.ResumeAsync(projectionName, TestContext.Current.CancellationToken);

        // Assert
        var status = await manager.GetStatusAsync(projectionName, TestContext.Current.CancellationToken);
        Assert.NotNull(status);
        Assert.Equal(ProjectionState.Active, status.State);
    }

    [Fact]
    public async Task RetryFailedEventAsync_ThrowsInvalidOperationException_WhenNotFaulted()
    {
        // Arrange
        var provider = BuildServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        var projectionName = typeof(TestProjection).FullName!;
        
        // Clean up and create an active projection status
        await db.Set<DbProjectionStatus>().Where(s => s.ProjectionName == projectionName)
            .ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        await CreateProjectionStatusAsync(db, projectionName, ProjectionState.Active);

        var manager = scope.ServiceProvider.GetRequiredService<IProjectionManager>();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.RetryFailedEventAsync(projectionName, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RetryFailedEventAsync_ClearsErrorAndSetsActive()
    {
        // Arrange
        var provider = BuildServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        var projectionName = typeof(TestProjection).FullName!;
        
        // Clean up and create a faulted projection status
        await db.Set<DbProjectionStatus>().Where(s => s.ProjectionName == projectionName)
            .ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        await CreateProjectionStatusAsync(db, projectionName, ProjectionState.Faulted, 
            lastError: "Test error", failedEventSequence: 42);

        var manager = scope.ServiceProvider.GetRequiredService<IProjectionManager>();

        // Act
        await manager.RetryFailedEventAsync(projectionName, TestContext.Current.CancellationToken);

        // Assert
        var status = await manager.GetStatusAsync(projectionName, TestContext.Current.CancellationToken);
        Assert.NotNull(status);
        Assert.Equal(ProjectionState.Active, status.State);
        Assert.Null(status.LastError);
        Assert.Null(status.FailedEventSequence);
    }

    [Fact]
    public async Task SkipFailedEventAsync_ThrowsInvalidOperationException_WhenNotFaulted()
    {
        // Arrange
        var provider = BuildServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        var projectionName = typeof(TestProjection).FullName!;
        
        // Clean up and create an active projection status
        await db.Set<DbProjectionStatus>().Where(s => s.ProjectionName == projectionName)
            .ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        await CreateProjectionStatusAsync(db, projectionName, ProjectionState.Active);

        var manager = scope.ServiceProvider.GetRequiredService<IProjectionManager>();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.SkipFailedEventAsync(projectionName, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SkipFailedEventAsync_AdvancesPositionAndClearsError()
    {
        // Arrange
        var provider = BuildServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        var projectionName = typeof(TestProjection).FullName!;
        
        // Clean up and create a faulted projection status
        await db.Set<DbProjectionStatus>().Where(s => s.ProjectionName == projectionName)
            .ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        await CreateProjectionStatusAsync(db, projectionName, ProjectionState.Faulted,
            position: 41, lastError: "Test error", failedEventSequence: 42);

        var manager = scope.ServiceProvider.GetRequiredService<IProjectionManager>();

        // Act
        await manager.SkipFailedEventAsync(projectionName, TestContext.Current.CancellationToken);

        // Assert
        var status = await manager.GetStatusAsync(projectionName, TestContext.Current.CancellationToken);
        Assert.NotNull(status);
        Assert.Equal(ProjectionState.Active, status.State);
        Assert.Equal(42, status.Position); // Position should advance to the failed event sequence
        Assert.Null(status.LastError);
        Assert.Null(status.FailedEventSequence);
    }

    [Fact]
    public async Task GetFailedEventAsync_ReturnsNull_WhenProjectionNotFaulted()
    {
        // Arrange
        var provider = BuildServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        var projectionName = typeof(TestProjection).FullName!;
        
        // Clean up and create an active projection status
        await db.Set<DbProjectionStatus>().Where(s => s.ProjectionName == projectionName)
            .ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        await CreateProjectionStatusAsync(db, projectionName, ProjectionState.Active);

        var manager = scope.ServiceProvider.GetRequiredService<IProjectionManager>();

        // Act
        var failedEvent = await manager.GetFailedEventAsync(projectionName, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(failedEvent);
    }

    [Fact]
    public async Task GetFailedEventAsync_ReturnsEventDetails_WhenProjectionFaulted()
    {
        // Arrange
        var provider = BuildServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        // Add an event to reference
        var streamId = Guid.NewGuid();
        db.Streams.StartStream(streamId, events: [new TestEvent { Value = "test" }]);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var dbEvent = await db.Events.FirstAsync(TestContext.Current.CancellationToken);

        var projectionName = typeof(TestProjection).FullName!;
        
        // Clean up and create a faulted projection status referencing the event
        await db.Set<DbProjectionStatus>().Where(s => s.ProjectionName == projectionName)
            .ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        await CreateProjectionStatusAsync(db, projectionName, ProjectionState.Faulted,
            lastError: "Test error message", failedEventSequence: dbEvent.Sequence);

        var manager = scope.ServiceProvider.GetRequiredService<IProjectionManager>();

        // Act
        var failedEvent = await manager.GetFailedEventAsync(projectionName, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(failedEvent);
        Assert.Equal(dbEvent.EventId, failedEvent.EventId);
        Assert.Equal(dbEvent.StreamId, failedEvent.StreamId);
        Assert.Equal(dbEvent.Sequence, failedEvent.Sequence);
        Assert.Equal("Test error message", failedEvent.ProjectionError);
    }
}

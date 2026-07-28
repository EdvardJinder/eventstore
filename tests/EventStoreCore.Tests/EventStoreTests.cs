using Microsoft.EntityFrameworkCore;
using Npgsql;
using EventStoreCore;

using EventStoreCore.Postgres;

using EventStoreCore.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace EventStoreCore.Tests;

public class EventStoreTests(EventStoreFixture eventStoreFixture)
    : IClassFixture<EventStoreFixture>, IAsyncLifetime
{
    public ValueTask InitializeAsync()
    {
        // Some tests recreate the shared database, which resets generated
        // sequences. Do not retain entities keyed by an earlier sequence.
        eventStoreFixture.Context.ChangeTracker.Clear();
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public class TestEvent
    {
        public string Name { get; set; } = "John Doe";
    }
    public record TestRecordEvent(string Name = "Mary Jane");

    public class TestState : IState
    {
        public string Name { get; private set; } = "Initial";
        public void Apply(IEvent @event)
        {
            switch (@event)
            {
                case Event<TestEvent> e:
                    Name = e.Data.Name;
                    break;
                case Event<TestRecordEvent> e:
                    Name = e.Data.Name;
                    break;
            }
        }
    }

    public class SnapshotState : IState
    {
        public string Name { get; set; } = "Initial";
        public int ApplyCount { get; set; }

        public void Apply(IEvent @event)
        {
            if (@event is Event<TestEvent> e)
            {
                Name = e.Data.Name;
                ApplyCount++;
            }
        }
    }

    public class SnapshotNameLengthState : IState
    {
        public int NameLength { get; set; }
        public int ApplyCount { get; set; }

        public void Apply(IEvent @event)
        {
            if (@event is Event<TestEvent> e)
            {
                NameLength = e.Data.Name.Length;
                ApplyCount++;
            }
        }
    }

    private static async Task<long> GetCurrentVersionAsync(
        EventStoreFixture.EventStoreDbContext dbContext,
        Guid streamId,
        Guid tenantId = default,
        string streamType = "")
    {
        var stream = await dbContext.Set<DbStream>()
            .AsNoTracking()
            .SingleAsync(
                s => s.Id == streamId && s.TenantId == tenantId && s.StreamType == streamType,
                TestContext.Current.CancellationToken);

        return stream.CurrentVersion;
    }

    private ServiceProvider CreateSnapshotProvider()
    {
        var services = new ServiceCollection();
        services.AddDbContext<EventStoreFixture.EventStoreDbContext>(options => options.UseNpgsql(eventStoreFixture.ConnectionString));
        services.AddEventStore(c =>
        {
            c.ExistingDbContext<EventStoreFixture.EventStoreDbContext>();
            c.UseSnapshots(snapshots =>
            {
                snapshots.For<SnapshotState>("orders", o => o.Interval = 2);
                snapshots.For<SnapshotNameLengthState>("orders", o => o.Interval = 3);
            });
        });

        return services.BuildServiceProvider();
    }

    private static async Task RecreateAsync(DbContext dbContext)
    {
        await dbContext.Database.EnsureDeletedAsync(TestContext.Current.CancellationToken);
        await dbContext.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CanStartStream()
    {

        var dbContext = eventStoreFixture.Context;

        var eventStore = dbContext.Streams;

        var id = Guid.NewGuid();
        eventStore.StartStream(id, events: [new TestEvent(), new TestRecordEvent()]);

        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var stream = await eventStore.FetchForReadingAsync(id, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(stream);
    }

    [Fact]
    public async Task CanAppendToStream()
    {
        var id = Guid.NewGuid();
        
        // Use one context to create the stream
        using (var dbContext = eventStoreFixture.CreateNewContext())
        {
            var eventStore = dbContext.Streams;
            eventStore.StartStream(id, events: [new TestEvent(), new TestRecordEvent()]);
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        
        // Use a fresh context to append
        using (var dbContext = eventStoreFixture.CreateNewContext())
        {
            var eventStore = dbContext.Streams;
            var stream = await eventStore.FetchForWritingAsync(id, cancellationToken: TestContext.Current.CancellationToken);
            Assert.NotNull(stream);
            stream!.Append(new TestEvent { Name = "Jane Doe" });
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        
        // Use yet another context to verify
        using (var dbContext = eventStoreFixture.CreateNewContext())
        {
            var eventStore = dbContext.Streams;
            var readStream = await eventStore.FetchForReadingAsync(id, cancellationToken: TestContext.Current.CancellationToken);
            Assert.NotNull(readStream);
            Assert.Equal(3, readStream!.Events.Count);
        }
    }

    [Fact]
    public async Task CanAppendToStreamWithTenantId()
    {
        Guid tenantId = Guid.NewGuid();
        var id = Guid.NewGuid();
        
        // Use one context to create the stream
        using (var dbContext = eventStoreFixture.CreateNewContext())
        {
            var eventStore = dbContext.Streams;
            eventStore.StartStream(id, tenantId, events: [new TestEvent(), new TestRecordEvent()]);
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        
        // Use a fresh context to append
        using (var dbContext = eventStoreFixture.CreateNewContext())
        {
            var eventStore = dbContext.Streams;
            var stream = await eventStore.FetchForWritingAsync(id, tenantId, cancellationToken: TestContext.Current.CancellationToken);
            Assert.NotNull(stream);
            stream!.Append(new TestEvent { Name = "Jane Doe" });
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        
        // Use yet another context to verify
        using (var dbContext = eventStoreFixture.CreateNewContext())
        {
            var eventStore = dbContext.Streams;
            var readStream = await eventStore.FetchForReadingAsync(id, tenantId, cancellationToken: TestContext.Current.CancellationToken);
            Assert.NotNull(readStream);
            Assert.Equal(3, readStream!.Events.Count);
        }
    }

    [Fact]
    public async Task CanReadToVersion()
    {
        var dbContext = eventStoreFixture.Context;
        var eventStore = dbContext.Streams;
        var id = Guid.NewGuid();
        eventStore.StartStream(id, events: [new TestEvent(), new TestRecordEvent(), new TestEvent()]);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        var stream = await eventStore.FetchForReadingAsync(id, version: 2, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(stream);
        Assert.Equal(2, stream!.Version);
        Assert.Equal(2, stream.Events.Count);

        // Verify that the events are the first two events
        Assert.IsType<IEvent<TestEvent>>(stream.Events[0], exactMatch: false);
        Assert.IsType<IEvent<TestRecordEvent>>(stream.Events[1], exactMatch: false);

    }

    [Fact] 
    public async Task CanReadEvents()
    {
        var dbContext = eventStoreFixture.Context;
        var eventStore = dbContext.Streams;
        var id = Guid.NewGuid();
        eventStore.StartStream(id, events: [new TestEvent(), new TestRecordEvent()]);

        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var stream = await eventStore.FetchForReadingAsync(id, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(stream);

        var events = stream.Events;

        Assert.Equal(2, events.Count);
        Assert.IsType<IEvent<TestEvent>>(events[0], exactMatch: false);
        Assert.IsType<Event<TestEvent>>(events[0]);
        Assert.IsType<TestEvent>(events[0].Data);
        Assert.IsType<IEvent<TestRecordEvent>>(events[1], exactMatch: false);
        Assert.IsType<Event<TestRecordEvent>>(events[1]);
        Assert.IsType<TestRecordEvent>(events[1].Data);
    }

    [Fact]
    public async Task CanBuildState()
    {
        var dbContext = eventStoreFixture.Context;
        var eventStore = dbContext.Streams;
        var id = Guid.NewGuid();
        eventStore.StartStream(id, events: [new TestEvent(), new TestRecordEvent()]);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        var stream = await eventStore.FetchForReadingAsync<TestState>(id, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(stream);
        Assert.NotNull(stream.State);
        Assert.Equal(2, stream.Version);
        Assert.Equal("Mary Jane", stream.State.Name);

    }

    [Fact]
    public async Task ConfiguredSnapshotsAreWrittenWhenAppendCrossesInterval()
    {
        using var provider = CreateSnapshotProvider();
        var streamId = Guid.NewGuid();

        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<EventStoreFixture.EventStoreDbContext>();
            await RecreateAsync(dbContext);

            await dbContext.Streams.AppendAsync(
                "orders",
                streamId,
                ExpectedVersion.NoStream,
                [new TestEvent { Name = "one" }, new TestEvent { Name = "two" }],
                TestContext.Current.CancellationToken);
        }

        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<EventStoreFixture.EventStoreDbContext>();
            var snapshots = await dbContext.Set<DbSnapshot>()
                .AsNoTracking()
                .Where(x => x.StreamId == streamId && x.StreamType == "orders")
                .ToListAsync(TestContext.Current.CancellationToken);

            var snapshot = Assert.Single(snapshots);
            Assert.Equal(typeof(SnapshotState).FullName, snapshot.StateType);
            Assert.Equal(2, snapshot.Version);
        }
    }

    [Fact]
    public async Task ConfiguredSnapshotsSupportMultipleStatesForOneStreamType()
    {
        using var provider = CreateSnapshotProvider();
        var streamId = Guid.NewGuid();

        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<EventStoreFixture.EventStoreDbContext>();
            await RecreateAsync(dbContext);

            await dbContext.Streams.AppendAsync(
                "orders",
                streamId,
                ExpectedVersion.NoStream,
                [new TestEvent { Name = "one" }, new TestEvent { Name = "two" }],
                TestContext.Current.CancellationToken);

            await dbContext.Streams.AppendAsync(
                "orders",
                streamId,
                ExpectedVersion.Exact(2),
                [new TestEvent { Name = "three" }],
                TestContext.Current.CancellationToken);
        }

        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<EventStoreFixture.EventStoreDbContext>();
            var snapshots = await dbContext.Set<DbSnapshot>()
                .AsNoTracking()
                .Where(x => x.StreamId == streamId && x.StreamType == "orders")
                .ToListAsync(TestContext.Current.CancellationToken);

            Assert.Equal(2, snapshots.Count);
            Assert.Contains(snapshots, x => x.StateType == typeof(SnapshotState).FullName && x.Version == 2);
            Assert.Contains(snapshots, x => x.StateType == typeof(SnapshotNameLengthState).FullName && x.Version == 3);
        }
    }

    [Fact]
    public async Task TypedReadUsesConfiguredSnapshotTransparently()
    {
        using var provider = CreateSnapshotProvider();
        var streamId = Guid.NewGuid();

        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<EventStoreFixture.EventStoreDbContext>();
            await RecreateAsync(dbContext);

            await dbContext.Streams.AppendAsync(
                "orders",
                streamId,
                ExpectedVersion.NoStream,
                [new TestEvent { Name = "one" }, new TestEvent { Name = "two" }],
                TestContext.Current.CancellationToken);

            await dbContext.Streams.AppendAsync(
                "orders",
                streamId,
                ExpectedVersion.Exact(2),
                [new TestEvent { Name = "three" }],
                TestContext.Current.CancellationToken);
        }

        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<EventStoreFixture.EventStoreDbContext>();
            var stream = await dbContext.Streams.FetchForReadingAsync<SnapshotState>(
                "orders",
                streamId,
                TestContext.Current.CancellationToken);

            Assert.NotNull(stream);
            Assert.Single(stream!.Events);
            Assert.Equal("three", stream.State.Name);
            Assert.Equal(3, stream.State.ApplyCount);
        }
    }

    [Fact]
    public async Task SnapshotBackedVersionedReadExposesOnlyReplayTail()
    {
        using var provider = CreateSnapshotProvider();
        var streamId = Guid.NewGuid();

        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<EventStoreFixture.EventStoreDbContext>();
            await RecreateAsync(dbContext);

            await dbContext.Streams.AppendAsync(
                "orders",
                streamId,
                ExpectedVersion.NoStream,
                [new TestEvent { Name = "one" }, new TestEvent { Name = "two" }],
                TestContext.Current.CancellationToken);

            await dbContext.Streams.AppendAsync(
                "orders",
                streamId,
                ExpectedVersion.Exact(2),
                [new TestEvent { Name = "three" }],
                TestContext.Current.CancellationToken);
        }

        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<EventStoreFixture.EventStoreDbContext>();
            var stream = await dbContext.Streams.FetchForReadingAsync<SnapshotState>(
                "orders",
                streamId,
                version: 2,
                TestContext.Current.CancellationToken);

            Assert.NotNull(stream);
            Assert.Equal(2, stream!.Version);
            Assert.Empty(stream.Events);
            Assert.Equal("two", stream.State.Name);
            Assert.Equal(2, stream.State.ApplyCount);
        }
    }

    [Fact]
    public async Task UnregisteredTypedReadFallsBackToFullReplay()
    {
        using var provider = CreateSnapshotProvider();
        var streamId = Guid.NewGuid();

        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<EventStoreFixture.EventStoreDbContext>();
            await RecreateAsync(dbContext);

            await dbContext.Streams.AppendAsync(
                "invoices",
                streamId,
                ExpectedVersion.NoStream,
                [new TestEvent { Name = "one" }, new TestEvent { Name = "two" }, new TestEvent { Name = "three" }],
                TestContext.Current.CancellationToken);
        }

        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<EventStoreFixture.EventStoreDbContext>();
            var stream = await dbContext.Streams.FetchForReadingAsync<SnapshotState>(
                "invoices",
                streamId,
                TestContext.Current.CancellationToken);

            Assert.NotNull(stream);
            Assert.Equal(3, stream!.Events.Count);
            Assert.Equal("three", stream.State.Name);
            Assert.Equal(3, stream.State.ApplyCount);
        }
    }

    [Fact]
    public async Task GracefullyHandlesNonExistantStream()
    {
        var dbContext = eventStoreFixture.Context;
        var eventStore = dbContext.Streams;
        var stream = await eventStore.FetchForReadingAsync(Guid.NewGuid(), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Null(stream);
        var stream2 = await eventStore.FetchForWritingAsync(Guid.NewGuid(), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Null(stream2);
        var stream3 = await eventStore.FetchForReadingAsync<TestState>(Guid.NewGuid(), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Null(stream3);
        var stream4 = await eventStore.FetchForWritingAsync<TestState>(Guid.NewGuid(), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Null(stream4);
    }

    [Fact]
    public async Task CanCreateMultipleStreamsWithSameIdButDifferentTypes()
    {
        var dbContext = eventStoreFixture.Context;
        var eventStore = dbContext.Streams;
        var id = Guid.NewGuid();

        // Create first stream with type "document-upload"
        eventStore.StartStream("document-upload", id, events: [new TestEvent { Name = "Upload Event" }]);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Create second stream with same ID but type "document-analysis"
        eventStore.StartStream("document-analysis", id, events: [new TestEvent { Name = "Analysis Event" }]);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Verify both streams exist independently
        var uploadStream = await eventStore.FetchForReadingAsync("document-upload", id, cancellationToken: TestContext.Current.CancellationToken);
        var analysisStream = await eventStore.FetchForReadingAsync("document-analysis", id, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(uploadStream);
        Assert.NotNull(analysisStream);
        Assert.Single(uploadStream!.Events);
        Assert.Single(analysisStream!.Events);
        
        var uploadEvent = uploadStream.Events[0] as Event<TestEvent>;
        var analysisEvent = analysisStream.Events[0] as Event<TestEvent>;
        
        Assert.Equal("Upload Event", uploadEvent?.Data.Name);
        Assert.Equal("Analysis Event", analysisEvent?.Data.Name);
    }

    [Fact]
    public async Task AppendAsync_NoStream_Throws_WhenStreamAlreadyExists()
    {
        var streamId = Guid.NewGuid();

        using (var dbContext = eventStoreFixture.CreateNewContext())
        {
            dbContext.Streams.StartStream(streamId, events: [new TestEvent()]);
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using var appendContext = eventStoreFixture.CreateNewContext();

        var exception = await Assert.ThrowsAsync<EventStoreConcurrencyException>(() =>
            appendContext.Streams.AppendAsync(
                streamId,
                ExpectedVersion.NoStream,
                [new TestEvent { Name = "Unexpected" }],
                TestContext.Current.CancellationToken));

        Assert.Equal(ExpectedVersion.NoStream, exception.ExpectedVersion);
        Assert.Equal(streamId, exception.StreamId);
        Assert.Equal(1, exception.ActualVersion);
    }

    [Fact]
    public async Task AppendAsync_StreamExists_Throws_WhenStreamDoesNotExist()
    {
        var streamId = Guid.NewGuid();

        using var dbContext = eventStoreFixture.CreateNewContext();

        var exception = await Assert.ThrowsAsync<EventStoreConcurrencyException>(() =>
            dbContext.Streams.AppendAsync(
                streamId,
                ExpectedVersion.StreamExists,
                [new TestEvent()],
                TestContext.Current.CancellationToken));

        Assert.Equal(ExpectedVersion.StreamExists, exception.ExpectedVersion);
        Assert.Equal(streamId, exception.StreamId);
        Assert.Null(exception.ActualVersion);
    }

    [Fact]
    public async Task AppendAsync_Exact_Throws_WhenVersionDoesNotMatch()
    {
        var streamId = Guid.NewGuid();

        using (var dbContext = eventStoreFixture.CreateNewContext())
        {
            dbContext.Streams.StartStream(streamId, events: [new TestEvent()]);
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using var appendContext = eventStoreFixture.CreateNewContext();

        var exception = await Assert.ThrowsAsync<EventStoreConcurrencyException>(() =>
            appendContext.Streams.AppendAsync(
                streamId,
                ExpectedVersion.Exact(0),
                [new TestEvent { Name = "Version mismatch" }],
                TestContext.Current.CancellationToken));

        Assert.Equal(ExpectedVersionMode.Exact, exception.ExpectedVersion.Mode);
        Assert.Equal(0, exception.ExpectedVersion.Version);
        Assert.Equal(1, exception.ActualVersion);
    }

    [Fact]
    public async Task AppendAsync_ConcurrentExactWriters_CauseOneConcurrencyFailure()
    {
        var streamId = Guid.NewGuid();

        using (var dbContext = eventStoreFixture.CreateNewContext())
        {
            dbContext.Streams.StartStream(streamId, events: [new TestEvent()]);
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<Exception?> AppendFromNewContext(string name)
        {
            using var dbContext = eventStoreFixture.CreateNewContext();
            await gate.Task;

            try
            {
                await dbContext.Streams.AppendAsync(
                    streamId,
                    ExpectedVersion.Exact(1),
                    [new TestEvent { Name = name }],
                    TestContext.Current.CancellationToken);
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        var writer1 = AppendFromNewContext("writer-1");
        var writer2 = AppendFromNewContext("writer-2");

        gate.SetResult();

        var results = await Task.WhenAll(writer1, writer2);
        var failures = results.OfType<Exception>().ToArray();

        Assert.Equal(1, results.Count(r => r is null));
        var failure = Assert.Single(failures);
        var concurrencyException = Assert.IsType<EventStoreConcurrencyException>(failure);
        Assert.Equal(2, concurrencyException.ActualVersion);

        using var verifyContext = eventStoreFixture.CreateNewContext();
        var readStream = await verifyContext.Streams.FetchForReadingAsync(streamId, TestContext.Current.CancellationToken);

        Assert.NotNull(readStream);
        Assert.Equal(2, readStream!.Events.Count);
        Assert.Equal(2, await GetCurrentVersionAsync(verifyContext, streamId));
    }

    [Fact]
    public async Task AppendAsync_DoesNotCommitUnrelatedTrackedChanges()
    {
        var streamId = Guid.NewGuid();
        var snapshot = new ProjectionTests.UserSnapshot
        {
            UserId = Guid.NewGuid(),
            Name = "pending"
        };

        using var dbContext = eventStoreFixture.CreateNewContext();
        dbContext.Add(snapshot);

        await dbContext.Streams.AppendAsync(
            streamId,
            ExpectedVersion.NoStream,
            [new TestEvent()],
            TestContext.Current.CancellationToken);

        Assert.Equal(EntityState.Added, dbContext.Entry(snapshot).State);

        using var verifyContext = eventStoreFixture.CreateNewContext();
        Assert.False(await verifyContext.Set<ProjectionTests.UserSnapshot>()
            .AnyAsync(x => x.UserId == snapshot.UserId, TestContext.Current.CancellationToken));
        Assert.NotNull(await verifyContext.Streams.FetchForReadingAsync(
            streamId,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public void StartStream_RejectsValueTypeEventPayloads()
    {
        using var dbContext = eventStoreFixture.CreateNewContext();

        var exception = Assert.Throws<ArgumentException>(() =>
            dbContext.Streams.StartStream(Guid.NewGuid(), events: [42]));

        Assert.Contains("reference types", exception.Message);
    }

    [Fact]
    public async Task CanAppendToStreamWithSpecificType()
    {
        var dbContext = eventStoreFixture.Context;
        var eventStore = dbContext.Streams;
        var id = Guid.NewGuid();
        var streamType = "document-lifecycle";

        // Create stream with specific type
        eventStore.StartStream(streamType, id, events: [new TestEvent { Name = "Created" }]);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Fetch and append more events
        var stream = await eventStore.FetchForWritingAsync(streamType, id, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(stream);
        stream!.Append(new TestEvent { Name = "Updated" });
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Verify all events are in the correct stream
        var readStream = await eventStore.FetchForReadingAsync(streamType, id, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(readStream);
        Assert.Equal(2, readStream!.Events.Count);
    }

    [Fact]
    public async Task DifferentStreamTypesShouldNotInterfere()
    {
        var dbContext = eventStoreFixture.Context;
        var eventStore = dbContext.Streams;
        var id = Guid.NewGuid();

        // Create two streams with same ID but different types
        eventStore.StartStream("type-a", id, events: [new TestEvent { Name = "Type A Event 1" }]);
        eventStore.StartStream("type-b", id, events: [new TestEvent { Name = "Type B Event 1" }]);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Append to type-a
        var streamA = await eventStore.FetchForWritingAsync("type-a", id, cancellationToken: TestContext.Current.CancellationToken);
        streamA!.Append(new TestEvent { Name = "Type A Event 2" });
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Verify type-a has 2 events
        var readStreamA = await eventStore.FetchForReadingAsync("type-a", id, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(2, readStreamA!.Events.Count);

        // Verify type-b still has only 1 event
        var readStreamB = await eventStore.FetchForReadingAsync("type-b", id, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Single(readStreamB!.Events);
    }

    [Fact]
    public async Task CanReadToVersionWithStreamType()
    {
        var dbContext = eventStoreFixture.Context;
        var eventStore = dbContext.Streams;
        var id = Guid.NewGuid();
        var streamType = "versioned-stream";

        eventStore.StartStream(streamType, id, events: [new TestEvent(), new TestRecordEvent(), new TestEvent()]);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var stream = await eventStore.FetchForReadingAsync(streamType, id, version: 2, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(stream);
        Assert.Equal(2, stream!.Events.Count);
    }

    [Fact]
    public async Task MultipleTenantsCanHaveSameStreamIdAndType()
    {
        // This test verifies that the primary key includes TenantId,
        // allowing different tenants to have streams with identical Id and StreamType
        var streamId = Guid.NewGuid();
        var streamType = "shared-type";
        var tenant1Id = Guid.NewGuid();
        var tenant2Id = Guid.NewGuid();
        
        // Create stream for tenant 1
        using (var dbContext = eventStoreFixture.CreateNewContext())
        {
            var eventStore = dbContext.Streams;
            eventStore.StartStream(streamType, streamId, tenant1Id, events: [new TestEvent { Name = "Tenant 1 Event" }]);
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        
        // Create stream with same Id and StreamType for tenant 2 (should not throw)
        using (var dbContext = eventStoreFixture.CreateNewContext())
        {
            var eventStore = dbContext.Streams;
            eventStore.StartStream(streamType, streamId, tenant2Id, events: [new TestEvent { Name = "Tenant 2 Event" }]);
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        
        // Verify tenant 1 stream
        using (var dbContext = eventStoreFixture.CreateNewContext())
        {
            var eventStore = dbContext.Streams;
            var stream1 = await eventStore.FetchForReadingAsync(streamType, streamId, tenant1Id, cancellationToken: TestContext.Current.CancellationToken);
            Assert.NotNull(stream1);
            Assert.Single(stream1!.Events);
            var event1 = stream1.Events[0] as IEvent<TestEvent>;
            Assert.NotNull(event1);
            Assert.Equal("Tenant 1 Event", event1!.Data.Name);
        }
        
        // Verify tenant 2 stream
        using (var dbContext = eventStoreFixture.CreateNewContext())
        {
            var eventStore = dbContext.Streams;
            var stream2 = await eventStore.FetchForReadingAsync(streamType, streamId, tenant2Id, cancellationToken: TestContext.Current.CancellationToken);
            Assert.NotNull(stream2);
            Assert.Single(stream2!.Events);
            var event2 = stream2.Events[0] as IEvent<TestEvent>;
            Assert.NotNull(event2);
            Assert.Equal("Tenant 2 Event", event2!.Data.Name);
        }
    }

    [Fact]
    public async Task CanAppendToTypedStreamWithSpecificStreamType()
    {
        // Reproduces bug: When using typed stream APIs (StartStream<T> + FetchForWritingAsync<T>)
        // with explicit stream types, the persisted StreamType can end up as empty string
        var streamId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var streamType = "tenant-lifecycle";
        
        // Step 1: Start a stream with explicit stream type using typed API
        using (var dbContext = eventStoreFixture.CreateNewContext())
        {
            var eventStore = dbContext.Streams;
            eventStore.StartStream<TestState>(streamType, streamId, tenantId, events: [new TestEvent { Name = "Tenant Registered" }]);
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        
        // Step 2: Verify the stream was created with the correct stream type
        using (var dbContext = eventStoreFixture.CreateNewContext())
        {
            var dbStream = await dbContext.Set<DbStream>()
                .Include(s => s.Events)
                .FirstOrDefaultAsync(s => s.Id == streamId && s.TenantId == tenantId, TestContext.Current.CancellationToken);
            
            Assert.NotNull(dbStream);
            Assert.Equal(streamType, dbStream!.StreamType); // Should be "tenant-lifecycle", not ""
            Assert.Single(dbStream.Events);
            Assert.Equal(streamType, dbStream.Events.First().StreamType); // Event should also have correct stream type
        }
        
        // Step 3: Fetch for writing with explicit stream type using typed API
        using (var dbContext = eventStoreFixture.CreateNewContext())
        {
            var eventStore = dbContext.Streams;
            var stream = await eventStore.FetchForWritingAsync<TestState>(streamType, streamId, tenantId, cancellationToken: TestContext.Current.CancellationToken);
            
            Assert.NotNull(stream);
            
            // Step 4: Append event and save
            stream!.Append(new TestEvent { Name = "Tenant Disabled" });
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken); // Should not throw DbUpdateConcurrencyException
        }
        
        // Step 5: Verify the second event was appended successfully with correct stream type
        using (var dbContext = eventStoreFixture.CreateNewContext())
        {
            var dbStream = await dbContext.Set<DbStream>()
                .Include(s => s.Events)
                .FirstOrDefaultAsync(s => s.Id == streamId && s.TenantId == tenantId && s.StreamType == streamType, TestContext.Current.CancellationToken);
            
            Assert.NotNull(dbStream);
            Assert.Equal(streamType, dbStream!.StreamType);
            Assert.Equal(2, dbStream.Events.Count);
            Assert.All(dbStream.Events, e => Assert.Equal(streamType, e.StreamType)); // All events should have correct stream type
        }
    }
}

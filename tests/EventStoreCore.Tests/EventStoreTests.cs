using Microsoft.EntityFrameworkCore;
using Npgsql;
using EventStoreCore;

using EventStoreCore.Postgres;

using EventStoreCore.Abstractions;

namespace EventStoreCore.Tests;

public class EventStoreTests(EventStoreFixture eventStoreFixture) : IClassFixture<EventStoreFixture>
{

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
        Assert.Equal(2, stream!.Events.Count);

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
}


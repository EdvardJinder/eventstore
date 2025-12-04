using Microsoft.EntityFrameworkCore;
using Npgsql;
using IM.EventStore.Persistence.EntityFrameworkCore.Postgres;
using IM.EventStore.Abstractions;

namespace IM.EventStore.Tests;

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
        var dbContext = eventStoreFixture.Context;
        var eventStore = dbContext.Streams;
        var id = Guid.NewGuid();
        eventStore.StartStream(id, events: [new TestEvent(), new TestRecordEvent()]);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        var stream = await eventStore.FetchForWritingAsync(id, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(stream);
        stream!.Append(new TestEvent { Name = "Jane Doe" });
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        var readStream = await eventStore.FetchForReadingAsync(id, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(readStream);
        Assert.Equal(3, readStream!.Events.Count);
    }

    [Fact]
    public async Task CanAppendToStreamWithTenantId()
    {
        Guid tenantId = Guid.NewGuid();
        var dbContext = eventStoreFixture.Context;
        var eventStore = dbContext.Streams;
        var id = Guid.NewGuid();
        eventStore.StartStream(id, tenantId, events: [new TestEvent(), new TestRecordEvent()]);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        var stream = await eventStore.FetchForWritingAsync(id, tenantId, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(stream);
        stream!.Append(new TestEvent { Name = "Jane Doe" });
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        var readStream = await eventStore.FetchForReadingAsync(id, tenantId, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(readStream);
        Assert.Equal(3, readStream!.Events.Count);
    }

    [Fact] 
    async Task CanReadEvents()
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
    async Task CanBuildState()
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
}


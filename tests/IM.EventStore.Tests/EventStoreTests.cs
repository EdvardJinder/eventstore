using Microsoft.EntityFrameworkCore;
using Npgsql;
using IM.EventStore.MassTransit;

namespace IM.EventStore.Tests;

public class EventStoreTests(EventStoreFixture eventStoreFixture) : IClassFixture<EventStoreFixture>
{

    public class TestEvent
    {
        public string Name { get; set; } = "John Doe";
    }
    public record TestRecordEvent(string Name = "Mary Jane");

    [Fact]
    public async Task CanStartStream()
    {

        var dbContext = eventStoreFixture.Context;

        var eventStore = dbContext.Events;

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
        var eventStore = dbContext.Events;
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
    async Task CanReadEvents()
    {
        var dbContext = eventStoreFixture.Context;
        var eventStore = dbContext.Events;
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
}


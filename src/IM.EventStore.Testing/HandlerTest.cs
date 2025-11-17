using IM.EventStore.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace IM.EventStore.Testing;

public abstract class HandlerTest<THandler, TState, TCommand>
    where THandler : IHandler<TState, TCommand>
    where TState : IState, new()
    where TCommand : class
{
    private readonly TState _state = new();
    private readonly int _version = 0;
    private readonly ICollection<IEvent> _committedEvents = new List<IEvent>();

    protected Guid StreamId { get; set; } = Guid.NewGuid();
    protected Guid TenantId { get; set; } = Guid.NewGuid();
    protected TimeProvider TimeProvider { get; set; } = new FakeTimeProvider(DateTimeOffset.UtcNow);
    protected HandlerTest(
        Guid? streamId = null,
        Guid? tenantId = null
        )
    {
        if (streamId is not null)
        {
            StreamId = streamId.Value;
        }
        if (tenantId is not null)
        {
            TenantId = tenantId.Value;
        }
    }

    protected void Given(params object[] events)
    {
        foreach (var @event in events)
        {
            var evnt = new DbEvent
            {
                Data = System.Text.Json.JsonSerializer.Serialize(@event),
                EventId = Guid.NewGuid(),
                StreamId = StreamId,
                TenantId = TenantId,
                Timestamp = TimeProvider.GetUtcNow(),
                Type = @event.GetType().AssemblyQualifiedName!,
                Version = _version + 1,
                Sequence = 0
            };
            _state.Apply(evnt.ToEvent());
        }
    }

    protected void When(TCommand command)
    {
        var events = THandler.Handle(_state, command);
        foreach (var @event in events)
        {
            var evnt = new DbEvent
            {
                Data = System.Text.Json.JsonSerializer.Serialize(@event),
                EventId = Guid.NewGuid(),
                StreamId = StreamId,
                TenantId = TenantId,
                Timestamp = TimeProvider.GetUtcNow(),
                Type = @event.GetType().AssemblyQualifiedName!,
                Version = _version + 1,
                Sequence = 0
            };
            _state.Apply(evnt.ToEvent());
            _committedEvents.Add(evnt.ToEvent());
        }
    }

    // Unordered, duplicate-aware comparison of produced vs expected events.
    // - Converts both sides to JsonNode to perform deterministic structural equality.
    // - Treats collections as multisets: ordering is ignored, duplicates are honored.
    protected void Then(params ICollection<object> expectedEvents)
    {
        var config = new KellermanSoftware.CompareNetObjects.ComparisonConfig
        {
            IgnoreCollectionOrder = true,   // ignore order
            MaxDifferences = 100,
            MaxMillisecondsDateDifference = 0
        };
        var compareLogic = new KellermanSoftware.CompareNetObjects.CompareLogic(config);
        var result = compareLogic.Compare(expectedEvents.ToArray(), _committedEvents.Select(x => x.Data).ToArray());
        result.AreEqual.ShouldBeTrue(result.DifferencesString);
    }

    protected void ThrowsWhen<TException>(TCommand command) where TException : Exception
    {
        Should.Throw<TException>(() => When(command));
    }

}

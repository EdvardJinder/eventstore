
using IM.EventStore.Abstractions;
using Shouldly;
using System;
using static org.apache.zookeeper.Watcher;

namespace IM.EventStore.Testing;

public abstract class HandlerTest<THandler, TState, TCommand>
    where THandler : IHandler<TState, TCommand>
    where TState : IState, new()
    where TCommand : class
{
    private readonly TState _state = new();
    private readonly int _version = 0;

    private readonly Guid _streamId = Guid.NewGuid();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly List<IEvent> _committedEvents = new List<IEvent>();
   
    protected readonly TimeProvider TimeProvider = TimeProvider.System;
    protected HandlerTest(
        TimeProvider? timeProvider = null,
        Guid? streamId = null,
        Guid? tenantId = null
        )
    {
        if (timeProvider is not null)
        {
            TimeProvider = timeProvider;
        }
        if (streamId is not null)
        {
            _streamId = streamId.Value;
        }
        if (tenantId is not null)
        {
            _tenantId = tenantId.Value;
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
                StreamId = _streamId,
                TenantId = _tenantId,
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
                StreamId = _streamId,
                TenantId = _tenantId,
                Timestamp = TimeProvider.GetUtcNow(),
                Type = @event.GetType().AssemblyQualifiedName!,
                Version = _version + 1,
                Sequence = 0
            };
            _state.Apply(evnt.ToEvent());
            _committedEvents.Add(evnt.ToEvent());
        }
    }

    protected void Then(params object[] expectedEvents)
    {
        _committedEvents.Count.ShouldBe(expectedEvents.Length, "Number of events produced does not match expected.");
        _committedEvents.Select(e => e.Data).ToArray().ShouldBe(expectedEvents, "Produced events do not match expected events.");
    }
    protected void ThrowsWhen<TException>(TCommand command) where TException : Exception
    {
        Should.Throw<TException>(() => When(command));
    }

}

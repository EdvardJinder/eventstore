using IM.EventStore.Abstractions;

namespace IM.EventStore;

public interface IHandler<TState, TCommand>
    where TState : IState, new()
    where TCommand : class
{
    public static abstract IReadOnlyCollection<object> Handle(TState state, TCommand command);
}

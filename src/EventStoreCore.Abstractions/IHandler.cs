namespace EventStoreCore.Abstractions;

public interface IHandler<TState, TCommand>
    where TState : IState, new()
    where TCommand : class
{
    public static abstract void Handle(IStream<TState> stream, TCommand command);
}

public interface IHandler<TCommand>
    where TCommand : class
{
    public static abstract IReadOnlyCollection<object> Handle(TCommand command);
}

/* TODO: Analyze if IHandler.Handle should return a result object like OneOf<T1, T2> instead of void and throwing exceptions.

API Proposal:

IStream<Account> stream = ...;

var result = DepositHandler.Handle(stream);

...
... result.Match(
    success => ...,
    err => ...
);

...

 */
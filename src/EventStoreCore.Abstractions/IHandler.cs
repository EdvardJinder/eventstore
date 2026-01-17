namespace EventStoreCore.Abstractions;

/// <summary>
/// Defines a command handler that appends events to a stateful stream.
/// </summary>
/// <typeparam name="TState">The stream state type.</typeparam>
/// <typeparam name="TCommand">The command type.</typeparam>
public interface IHandler<TState, TCommand>
    where TState : IState, new()
    where TCommand : class
{
    /// <summary>
    /// Handles a command by appending events to the provided stream.
    /// </summary>
    /// <param name="stream">The target stream.</param>
    /// <param name="command">The command to handle.</param>
    public static abstract void Handle(IStream<TState> stream, TCommand command);
}

/// <summary>
/// Defines a command handler that returns events to be persisted.
/// </summary>
/// <typeparam name="TCommand">The command type.</typeparam>
public interface IHandler<TCommand>
    where TCommand : class
{
    /// <summary>
    /// Handles a command and returns the events to persist.
    /// </summary>
    /// <param name="command">The command to handle.</param>
    /// <returns>The events produced by the command.</returns>
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
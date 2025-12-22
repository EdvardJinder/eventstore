namespace IM.EventStore.Abstractions;

/// <summary>
/// Options for configuring which events a projection handles and how keys are derived.
/// </summary>
public interface IProjectionOptions
{
    void Handles<T>() where T : class;
    void HandlesAll();
    void Handles<TEvent>(Func<IEvent<TEvent>, object>? keySelector = default) where TEvent : class;
}

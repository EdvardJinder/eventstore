namespace IM.EventStore;

public interface IProjectionOptions : ISubscriptionOptions
{
    void Handles<TEvent>(Func<IEvent<TEvent>, object>? keySelector = default) where TEvent : class;
}

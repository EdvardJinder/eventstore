namespace IM.EventStore;

internal sealed class SubscriptionOptions : ISubscriptionOptions, IProjectionOptions
{
    private readonly HashSet<Type> _handledEventTypes = new();
    private bool HandlesAllEvents = true;
    public void Handles<T>() where T : class
    {
        HandlesAllEvents = false;
        _handledEventTypes.Add(typeof(T));
    }

    public void HandlesAll()
    {
        HandlesAllEvents = true;
    }

    public bool IsHandeled(Type eventType)
    {
        return HandlesAllEvents || _handledEventTypes.Contains(eventType);
    }
}
namespace IM.EventStore;

internal class SubscriptionOptions : IConfigureSubscription
{
    public readonly HashSet<Type> Types = new();
    public bool HandlesAll;
    public bool StartFromPresent;
    public DateTimeOffset? StartFromTimestamp;
    public void Handles<T>() where T : class
    {
        Types.Add(typeof(T));
    }
    public void HandlesAllEvents()
    {
        HandlesAll = true;
    }
    public void SubscribeFrom(DateTimeOffset timestamp)
    {
        StartFromTimestamp = timestamp;
    }

    public void SubscribeFromPresent()
    {
        StartFromPresent = true;
    }
}
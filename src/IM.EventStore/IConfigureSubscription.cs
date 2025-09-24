namespace IM.EventStore;

public interface IConfigureSubscription
{
    void Handles<T>() where T : class;

    void HandlesAllEvents();

    void SubscribeFromPresent();
    void SubscribeFrom(DateTimeOffset timestamp);
}

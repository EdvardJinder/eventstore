namespace IM.EventStore;

public interface ISubscriptionOptions
{
    public void Handles<T>() where T : class;
    public void HandlesAll();
}

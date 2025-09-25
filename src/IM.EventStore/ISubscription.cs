namespace IM.EventStore;

public interface ISubscription
{
    Task HandleBatchAsync(IEvent[] events, CancellationToken ct);
}


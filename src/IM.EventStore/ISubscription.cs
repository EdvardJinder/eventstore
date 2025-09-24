using Microsoft.Extensions.Logging;

namespace IM.EventStore;

public interface ISubscription
{
    Task OnEventAsync(IEvent @event, CancellationToken cancellationToken);
}

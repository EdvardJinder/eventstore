using EventStoreCore.Abstractions;
using EventStoreCore.Scheduling;
using Microsoft.Extensions.Options;

namespace EventStoreCore.TickerQ;

internal sealed class TickerQSubscription(
    ISchedulerExecutionAdapter service,
    IOptions<SchedulerOptions> options) : ISubscription
{
    private readonly IReadOnlyList<IEventScheduleRegistration> _registrations = options.Value.Registrations;

    public async Task Handle(IEvent @event, CancellationToken ct)
    {
        foreach (var registration in _registrations)
        {
            if (registration.EventType != @event.EventType)
            {
                continue;
            }

            await registration.ApplyAsync(@event, service, ct);
        }
    }
}

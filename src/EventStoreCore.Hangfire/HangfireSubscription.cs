using EventStoreCore.Abstractions;
using EventStoreCore.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EventStoreCore.Hangfire;

internal sealed class HangfireSubscription(
    IServiceScopeFactory scopeFactory,
    ISchedulerEventApplicationStore applicationStore,
    IOptions<SchedulerOptions> options) : ISubscription
{
    private readonly IReadOnlyList<IEventSchedulerRegistration> _registrations = options.Value.Registrations;

    public async Task Handle(IEvent @event, CancellationToken ct)
    {
        foreach (var registration in _registrations)
        {
            if (registration.EventType != @event.EventType)
            {
                continue;
            }

            if (registration.ProviderName != HangfireSchedulerExtensions.ProviderName)
            {
                continue;
            }

            await using var scope = scopeFactory.CreateAsyncScope();
            await registration.ApplyAsync(@event, scope.ServiceProvider, applicationStore, ct);
        }
    }
}

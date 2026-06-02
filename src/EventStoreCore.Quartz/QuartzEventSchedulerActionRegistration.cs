using EventStoreCore.Abstractions;
using EventStoreCore.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Quartz;

namespace EventStoreCore.Quartz;

internal sealed class QuartzEventSchedulerActionRegistration<TEvent>(
    string registrationName,
    Func<IEvent<TEvent>, IScheduler, IServiceProvider, CancellationToken, ValueTask> action)
    : IEventSchedulerRegistration
    where TEvent : class
{
    public string ProviderName => QuartzSchedulerExtensions.ProviderName;

    public string RegistrationName { get; } = registrationName;

    public Type EventType => typeof(TEvent);

    public async Task ApplyAsync(
        IEvent @event,
        IServiceProvider serviceProvider,
        ISchedulerEventApplicationStore applicationStore,
        CancellationToken ct)
    {
        var typedEvent = (IEvent<TEvent>)@event;
        var claimId = Guid.NewGuid();
        var shouldApply = await applicationStore.TryClaimAsync(
            ProviderName,
            RegistrationName,
            typedEvent.TenantId,
            typedEvent.Id,
            claimId,
            ct);
        if (!shouldApply)
        {
            return;
        }

        try
        {
            var scheduler = await serviceProvider.GetRequiredService<ISchedulerFactory>().GetScheduler(ct);
            await action(typedEvent, scheduler, serviceProvider, ct);
        }
        catch
        {
            await applicationStore.AbandonAsync(
                ProviderName,
                RegistrationName,
                typedEvent.TenantId,
                typedEvent.Id,
                claimId,
                CancellationToken.None);
            throw;
        }

        await applicationStore.CompleteAsync(
            ProviderName,
            RegistrationName,
            typedEvent.TenantId,
            typedEvent.Id,
            claimId,
            ct);
    }
}

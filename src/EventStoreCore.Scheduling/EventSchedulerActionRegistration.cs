using EventStoreCore.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace EventStoreCore.Scheduling;

internal sealed class EventSchedulerActionRegistration<TEvent, TProvider>(
    string providerName,
    string registrationName,
    Func<IEvent<TEvent>, TProvider, IServiceProvider, CancellationToken, ValueTask> action)
    : IEventSchedulerRegistration
    where TEvent : class
    where TProvider : notnull
{
    public string ProviderName { get; } = providerName;

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
            var provider = serviceProvider.GetRequiredService<TProvider>();
            await action(typedEvent, provider, serviceProvider, ct);
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

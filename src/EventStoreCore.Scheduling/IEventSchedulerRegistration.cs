using EventStoreCore.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace EventStoreCore.Scheduling;

internal interface IEventSchedulerRegistration
{
    string ProviderName { get; }

    string RegistrationName { get; }

    Type EventType { get; }

    Task ApplyAsync(
        IEvent @event,
        IServiceProvider serviceProvider,
        ISchedulerEventApplicationStore applicationStore,
        CancellationToken ct);
}

namespace EventStoreCore.Scheduling;

internal interface ISchedulerEventApplicationStore
{
    Task<bool> TryClaimAsync(
        string providerName,
        string registrationName,
        Guid tenantId,
        Guid eventId,
        Guid claimId,
        CancellationToken ct);

    Task CompleteAsync(
        string providerName,
        string registrationName,
        Guid tenantId,
        Guid eventId,
        Guid claimId,
        CancellationToken ct);

    Task AbandonAsync(
        string providerName,
        string registrationName,
        Guid tenantId,
        Guid eventId,
        Guid claimId,
        CancellationToken ct);
}

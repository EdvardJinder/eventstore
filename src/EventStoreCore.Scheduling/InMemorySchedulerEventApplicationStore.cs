using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace EventStoreCore.Scheduling;

internal sealed class InMemorySchedulerEventApplicationStore(IOptions<SchedulerOptions> options) : ISchedulerEventApplicationStore
{
    private readonly ConcurrentDictionary<(string ProviderName, string RegistrationName, Guid TenantId, Guid EventId), SchedulerEventApplicationClaim> _claims = new();

    public Task<bool> TryClaimAsync(
        string providerName,
        string registrationName,
        Guid tenantId,
        Guid eventId,
        Guid claimId,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var key = (providerName, registrationName, tenantId, eventId);
        var now = DateTime.UtcNow;
        var claim = new SchedulerEventApplicationClaim(claimId, now, CompletedAtUtc: null);

        if (_claims.TryAdd(key, claim))
        {
            return Task.FromResult(true);
        }

        while (_claims.TryGetValue(key, out var existing))
        {
            if (existing.CompletedAtUtc is not null || now - existing.CreatedAtUtc <= options.Value.ClaimTimeout)
            {
                return Task.FromResult(false);
            }

            if (_claims.TryUpdate(key, claim, existing))
            {
                return Task.FromResult(true);
            }
        }

        return Task.FromResult(_claims.TryAdd(key, claim));
    }

    public Task CompleteAsync(
        string providerName,
        string registrationName,
        Guid tenantId,
        Guid eventId,
        Guid claimId,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var key = (providerName, registrationName, tenantId, eventId);
        while (_claims.TryGetValue(key, out var existing))
        {
            if (existing.ClaimId != claimId)
            {
                return Task.CompletedTask;
            }

            var completed = existing with { CompletedAtUtc = DateTime.UtcNow };
            if (_claims.TryUpdate(key, completed, existing))
            {
                return Task.CompletedTask;
            }
        }

        return Task.CompletedTask;
    }

    public Task AbandonAsync(
        string providerName,
        string registrationName,
        Guid tenantId,
        Guid eventId,
        Guid claimId,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var key = (providerName, registrationName, tenantId, eventId);
        while (_claims.TryGetValue(key, out var existing))
        {
            if (existing.ClaimId != claimId || existing.CompletedAtUtc is not null)
            {
                return Task.CompletedTask;
            }

            if (_claims.TryRemove(new KeyValuePair<(string ProviderName, string RegistrationName, Guid TenantId, Guid EventId), SchedulerEventApplicationClaim>(
                key,
                existing)))
            {
                return Task.CompletedTask;
            }
        }

        return Task.CompletedTask;
    }

    private sealed record SchedulerEventApplicationClaim(Guid ClaimId, DateTime CreatedAtUtc, DateTime? CompletedAtUtc);
}

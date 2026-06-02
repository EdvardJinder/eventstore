using EventStoreCore.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EventStoreCore;

internal sealed class EfCoreSchedulerEventApplicationStore<TDbContext>(
    IServiceScopeFactory scopeFactory,
    IOptions<SchedulerOptions> options)
    : ISchedulerEventApplicationStore
    where TDbContext : DbContext
{
    public async Task<bool> TryClaimAsync(
        string providerName,
        string registrationName,
        Guid tenantId,
        Guid eventId,
        Guid claimId,
        CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();

        var existing = await dbContext.Set<DbSchedulerEventApplication>()
            .SingleOrDefaultAsync(x => x.ProviderName == providerName &&
                                       x.RegistrationName == registrationName &&
                                       x.TenantId == tenantId &&
                                       x.EventId == eventId,
                ct);
        if (existing is { CompletedAtUtc: not null })
        {
            return false;
        }

        var now = DateTime.UtcNow;
        var cutoff = now - options.Value.ClaimTimeout;
        if (existing is { CreatedAtUtc: var createdAtUtc } && createdAtUtc > cutoff)
        {
            return false;
        }

        if (existing is not null)
        {
            var recovered = await dbContext.Set<DbSchedulerEventApplication>()
                .Where(x => x.ProviderName == providerName &&
                            x.RegistrationName == registrationName &&
                            x.TenantId == tenantId &&
                            x.EventId == eventId &&
                            x.CompletedAtUtc == null &&
                            x.CreatedAtUtc <= cutoff)
                .ExecuteUpdateAsync(updates => updates
                    .SetProperty(x => x.ClaimId, claimId)
                    .SetProperty(x => x.CreatedAtUtc, now),
                    ct);

            return recovered == 1;
        }

        dbContext.Add(new DbSchedulerEventApplication
        {
            ProviderName = providerName,
            RegistrationName = registrationName,
            TenantId = tenantId,
            EventId = eventId,
            ClaimId = claimId,
            CreatedAtUtc = now
        });

        try
        {
            await dbContext.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            if (await dbContext.Set<DbSchedulerEventApplication>()
                    .AnyAsync(x => x.ProviderName == providerName &&
                                   x.RegistrationName == registrationName &&
                                   x.TenantId == tenantId &&
                                   x.EventId == eventId,
                        CancellationToken.None))
            {
                return false;
            }

            throw;
        }
    }

    public async Task CompleteAsync(
        string providerName,
        string registrationName,
        Guid tenantId,
        Guid eventId,
        Guid claimId,
        CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();

        var entity = await dbContext.Set<DbSchedulerEventApplication>()
            .SingleOrDefaultAsync(x => x.ProviderName == providerName &&
                                       x.RegistrationName == registrationName &&
                                       x.TenantId == tenantId &&
                                       x.EventId == eventId &&
                                       x.ClaimId == claimId,
                ct);
        if (entity is null)
        {
            return;
        }

        entity.CompletedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task AbandonAsync(
        string providerName,
        string registrationName,
        Guid tenantId,
        Guid eventId,
        Guid claimId,
        CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();

        var entity = await dbContext.Set<DbSchedulerEventApplication>()
            .SingleOrDefaultAsync(x => x.ProviderName == providerName &&
                                       x.RegistrationName == registrationName &&
                                       x.TenantId == tenantId &&
                                       x.EventId == eventId &&
                                       x.ClaimId == claimId &&
                                       x.CompletedAtUtc == null,
                ct);
        if (entity is null)
        {
            return;
        }

        dbContext.Remove(entity);
        await dbContext.SaveChangesAsync(ct);
    }
}

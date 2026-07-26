using EventStoreCore.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace EventStoreCore;

internal sealed class DbContextStreamLifecycleManager(DbContext db) : IStreamLifecycleManager
{
    public async Task<StreamLifecycleInfo?> GetAsync(
        string streamType,
        Guid streamId,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(streamType);

        var stream = await db.Set<DbStream>()
            .AsNoTracking()
            .Include(x => x.LifecycleEntries)
            .SingleOrDefaultAsync(
                x => x.Id == streamId && x.StreamType == streamType && x.TenantId == tenantId,
                cancellationToken);

        return stream is null ? null : ToInfo(stream);
    }

    public Task<StreamLifecycleInfo> ArchiveAsync(
        string streamType,
        Guid streamId,
        Guid tenantId,
        long expectedVersion,
        StreamLifecycleChange change,
        CancellationToken cancellationToken = default)
        => TransitionAsync(
            streamType,
            streamId,
            tenantId,
            expectedVersion,
            StreamLifecycleState.Archived,
            change,
            cancellationToken);

    public Task<StreamLifecycleInfo> RestoreAsync(
        string streamType,
        Guid streamId,
        Guid tenantId,
        long expectedVersion,
        StreamLifecycleChange change,
        CancellationToken cancellationToken = default)
        => TransitionAsync(
            streamType,
            streamId,
            tenantId,
            expectedVersion,
            StreamLifecycleState.Active,
            change,
            cancellationToken);

    public Task<StreamLifecycleInfo> TombstoneAsync(
        string streamType,
        Guid streamId,
        Guid tenantId,
        long expectedVersion,
        StreamLifecycleChange change,
        CancellationToken cancellationToken = default)
        => TransitionAsync(
            streamType,
            streamId,
            tenantId,
            expectedVersion,
            StreamLifecycleState.Tombstoned,
            change,
            cancellationToken);

    private async Task<StreamLifecycleInfo> TransitionAsync(
        string streamType,
        Guid streamId,
        Guid tenantId,
        long expectedVersion,
        StreamLifecycleState targetState,
        StreamLifecycleChange change,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(streamType);
        ArgumentNullException.ThrowIfNull(change);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedVersion);
        ValidateChange(change);
        ValidateNoPendingEventStoreWrites();

        var stream = await db.Set<DbStream>()
            .AsNoTracking()
            .Include(x => x.LifecycleEntries)
            .SingleOrDefaultAsync(
                x => x.Id == streamId && x.StreamType == streamType && x.TenantId == tenantId,
                cancellationToken);

        if (stream is null)
        {
            throw Conflict(
                streamType,
                streamId,
                tenantId,
                expectedVersion,
                null,
                null,
                "The lifecycle transition expected an existing stream, but the stream was not found.");
        }

        if (stream.CurrentVersion != expectedVersion)
        {
            throw Conflict(
                streamType,
                streamId,
                tenantId,
                expectedVersion,
                stream.CurrentVersion,
                stream.LifecycleState,
                $"The lifecycle transition expected stream version {expectedVersion}, but observed {stream.CurrentVersion}.");
        }

        if (!CanTransition(stream.LifecycleState, targetState))
        {
            throw Conflict(
                streamType,
                streamId,
                tenantId,
                expectedVersion,
                stream.CurrentVersion,
                stream.LifecycleState,
                $"Stream lifecycle transition from {stream.LifecycleState} to {targetState} is not allowed.");
        }

        var changedAt = DateTimeOffset.UtcNow;
        var entry = new DbStreamLifecycleEntry
        {
            Id = Guid.NewGuid(),
            StreamId = stream.Id,
            StreamType = stream.StreamType,
            TenantId = stream.TenantId,
            FromState = stream.LifecycleState,
            ToState = targetState,
            StreamVersion = stream.CurrentVersion,
            ChangedAtUtc = changedAt,
            Actor = change.Actor.Trim(),
            Reason = change.Reason.Trim(),
            CorrelationId = string.IsNullOrWhiteSpace(change.CorrelationId)
                ? null
                : change.CorrelationId.Trim()
        };

        var currentTransaction = db.Database.CurrentTransaction;
        var ownsTransaction = currentTransaction is null;
        await using var transaction = ownsTransaction
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;
        const string savepointName = "EventStoreCore_StreamLifecycle";
        if (!ownsTransaction)
        {
            if (!currentTransaction!.SupportsSavepoints)
            {
                throw new InvalidOperationException(
                    "The current database transaction does not support the savepoint required for an audited lifecycle transition.");
            }

            await currentTransaction.CreateSavepointAsync(savepointName, cancellationToken);
        }

        try
        {
            var affected = await db.Set<DbStream>()
                .Where(x => x.Id == streamId
                    && x.StreamType == streamType
                    && x.TenantId == tenantId
                    && x.CurrentVersion == expectedVersion
                    && x.LifecycleState == stream.LifecycleState)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(x => x.LifecycleState, targetState)
                        .SetProperty(x => x.UpdatedTimestamp, changedAt),
                    cancellationToken);

            if (affected != 1)
            {
                var observed = await GetObservedAsync(streamType, streamId, tenantId, cancellationToken);
                throw Conflict(
                    streamType,
                    streamId,
                    tenantId,
                    expectedVersion,
                    observed.Version,
                    observed.State,
                    "The lifecycle transition failed because the stream changed concurrently.");
            }

            var unrelatedChanges = CaptureUnrelatedChanges();
            db.Set<DbStreamLifecycleEntry>().Add(entry);
            SuppressUnrelatedChanges(unrelatedChanges);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            finally
            {
                RestoreUnrelatedChanges(unrelatedChanges);
            }

            if (ownsTransaction)
            {
                await transaction!.CommitAsync(cancellationToken);
            }
            else
            {
                await currentTransaction!.ReleaseSavepointAsync(savepointName, cancellationToken);
            }
        }
        catch
        {
            if (ownsTransaction)
            {
                await transaction!.RollbackAsync(CancellationToken.None);
            }
            else
            {
                await currentTransaction!.RollbackToSavepointAsync(savepointName, CancellationToken.None);
                await currentTransaction.ReleaseSavepointAsync(savepointName, CancellationToken.None);
            }

            db.Entry(entry).State = EntityState.Detached;
            throw;
        }

        SynchronizeTrackedStream(streamType, streamId, tenantId, targetState, changedAt);
        stream.LifecycleState = targetState;
        stream.UpdatedTimestamp = changedAt;
        stream.LifecycleEntries.Add(entry);
        return ToInfo(stream);
    }

    private void SynchronizeTrackedStream(
        string streamType,
        Guid streamId,
        Guid tenantId,
        StreamLifecycleState state,
        DateTimeOffset updatedAt)
    {
        var tracked = db.ChangeTracker.Entries<DbStream>()
            .SingleOrDefault(x => x.Entity.Id == streamId
                && x.Entity.StreamType == streamType
                && x.Entity.TenantId == tenantId);
        if (tracked is null)
        {
            return;
        }

        tracked.Entity.LifecycleState = state;
        tracked.Entity.UpdatedTimestamp = updatedAt;
        tracked.OriginalValues.SetValues(tracked.CurrentValues);
        tracked.State = EntityState.Unchanged;
    }

    private IReadOnlyList<SuppressedEntry> CaptureUnrelatedChanges()
        => db.ChangeTracker.Entries()
            .Where(entry => entry.Entity is not DbStreamLifecycleEntry
                && entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(entry => new SuppressedEntry(entry.Entity, entry.State))
            .ToArray();

    private void SuppressUnrelatedChanges(IEnumerable<SuppressedEntry> entries)
    {
        foreach (var entry in entries)
        {
            db.Entry(entry.Entity).State = entry.State == EntityState.Added
                ? EntityState.Detached
                : EntityState.Unchanged;
        }
    }

    private void RestoreUnrelatedChanges(IEnumerable<SuppressedEntry> entries)
    {
        foreach (var entry in entries)
        {
            db.Entry(entry.Entity).State = entry.State;
        }
    }

    private sealed record SuppressedEntry(object Entity, EntityState State);

    private static bool CanTransition(StreamLifecycleState current, StreamLifecycleState target)
        => (current, target) switch
        {
            (StreamLifecycleState.Active, StreamLifecycleState.Archived) => true,
            (StreamLifecycleState.Archived, StreamLifecycleState.Active) => true,
            (StreamLifecycleState.Active, StreamLifecycleState.Tombstoned) => true,
            (StreamLifecycleState.Archived, StreamLifecycleState.Tombstoned) => true,
            _ => false
        };

    private static void ValidateChange(StreamLifecycleChange change)
    {
        if (string.IsNullOrWhiteSpace(change.Actor))
        {
            throw new ArgumentException("A lifecycle transition actor is required.", nameof(change));
        }

        if (string.IsNullOrWhiteSpace(change.Reason))
        {
            throw new ArgumentException("A lifecycle transition reason is required.", nameof(change));
        }

        if (change.Actor.Trim().Length > 500)
        {
            throw new ArgumentException("A lifecycle transition actor cannot exceed 500 characters.", nameof(change));
        }

        if (change.Reason.Trim().Length > 2000)
        {
            throw new ArgumentException("A lifecycle transition reason cannot exceed 2000 characters.", nameof(change));
        }

        if (change.CorrelationId?.Trim().Length > 500)
        {
            throw new ArgumentException("A lifecycle transition correlation identifier cannot exceed 500 characters.", nameof(change));
        }
    }

    private void ValidateNoPendingEventStoreWrites()
    {
        var hasPendingWrites = db.ChangeTracker.Entries()
            .Any(entry => (entry.Entity is DbStream or DbEvent or DbSnapshot)
                && entry.State is (EntityState.Added or EntityState.Modified or EntityState.Deleted));
        if (hasPendingWrites)
        {
            throw new InvalidOperationException(
                "Persist pending event-store writes before applying a stream lifecycle transition.");
        }
    }

    private async Task<(long? Version, StreamLifecycleState? State)> GetObservedAsync(
        string streamType,
        Guid streamId,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var observed = await db.Set<DbStream>()
            .AsNoTracking()
            .Where(x => x.Id == streamId && x.StreamType == streamType && x.TenantId == tenantId)
            .Select(x => new { x.CurrentVersion, x.LifecycleState })
            .SingleOrDefaultAsync(cancellationToken);

        return observed is null
            ? (null, null)
            : (observed.CurrentVersion, observed.LifecycleState);
    }

    private static StreamLifecycleInfo ToInfo(DbStream stream)
        => new()
        {
            StreamType = stream.StreamType,
            StreamId = stream.Id,
            TenantId = stream.TenantId,
            StreamVersion = stream.CurrentVersion,
            State = stream.LifecycleState,
            CreatedAtUtc = stream.CreatedTimestamp,
            UpdatedAtUtc = stream.UpdatedTimestamp,
            History = stream.LifecycleEntries
                .OrderBy(x => x.ChangedAtUtc)
                .ThenBy(x => x.Id)
                .Select(x => new StreamLifecycleEntry
                {
                    Id = x.Id,
                    FromState = x.FromState,
                    ToState = x.ToState,
                    StreamVersion = x.StreamVersion,
                    ChangedAtUtc = x.ChangedAtUtc,
                    Actor = x.Actor,
                    Reason = x.Reason,
                    CorrelationId = x.CorrelationId
                })
                .ToArray()
        };

    private static StreamLifecycleConflictException Conflict(
        string streamType,
        Guid streamId,
        Guid tenantId,
        long expectedVersion,
        long? actualVersion,
        StreamLifecycleState? actualState,
        string message,
        Exception? innerException = null)
        => new(
            streamType,
            streamId,
            tenantId,
            expectedVersion,
            actualVersion,
            actualState,
            message,
            innerException);
}

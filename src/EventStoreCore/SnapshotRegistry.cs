using EventStoreCore.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace EventStoreCore;

internal sealed class SnapshotRegistry
{
    private readonly IReadOnlyList<SnapshotRegistration> _registrations;

    public SnapshotRegistry(IEnumerable<SnapshotRegistration> registrations)
    {
        _registrations = registrations.ToArray();
        var duplicate = _registrations
            .GroupBy(r => new { r.StreamType, r.StateType })
            .FirstOrDefault(g => g.Count() > 1);

        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Snapshot state '{duplicate.Key.StateType.FullName ?? duplicate.Key.StateType.Name}' is already registered for stream type '{duplicate.Key.StreamType}'.");
        }
    }

    public bool HasRegistrations => _registrations.Count > 0;

    public IReadOnlyList<SnapshotRegistration> GetForStreamType(string streamType)
        => _registrations.Where(r => r.StreamType == streamType).ToArray();

    public SnapshotRegistration? GetForState(string streamType, Type stateType)
        => _registrations.FirstOrDefault(r => r.StreamType == streamType && r.StateType == stateType);

    public void SaveSnapshots(DbContext db, DbStream stream, long previousVersion)
    {
        foreach (var registration in GetForStreamType(stream.StreamType))
        {
            if (registration.ShouldSnapshot(previousVersion, stream.CurrentVersion))
            {
                registration.SaveSnapshot(db, stream);
            }
        }
    }

    public DbSnapshot? LoadSnapshot<TState>(
        DbContext db,
        string streamType,
        Guid streamId,
        Guid tenantId)
        where TState : IState, new()
    {
        var registration = GetForState(streamType, typeof(TState));
        if (registration is null)
        {
            return null;
        }

        return db.Set<DbSnapshot>()
            .AsNoTracking()
            .SingleOrDefault(x => x.StreamId == streamId
                && x.StreamType == streamType
                && x.TenantId == tenantId
                && x.StateType == registration.StateTypeName);
    }
}

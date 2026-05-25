using EventStoreCore.Abstractions;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace EventStoreCore;

internal abstract class SnapshotRegistration
{
    protected SnapshotRegistration(string streamType, Type stateType, int interval)
    {
        StreamType = streamType;
        StateType = stateType;
        StateTypeName = stateType.FullName
            ?? throw new InvalidOperationException($"State type '{stateType.Name}' does not have a full name.");
        Interval = interval;
    }

    public string StreamType { get; }

    public Type StateType { get; }

    public string StateTypeName { get; }

    public int Interval { get; }

    public bool ShouldSnapshot(long previousVersion, long currentVersion)
        => previousVersion / Interval < currentVersion / Interval;

    public abstract void SaveSnapshot(DbContext db, DbStream stream);
}

internal sealed class SnapshotRegistration<TState> : SnapshotRegistration
    where TState : IState, new()
{
    public SnapshotRegistration(string streamType, int interval)
        : base(streamType, typeof(TState), interval)
    {
    }

    public override void SaveSnapshot(DbContext db, DbStream stream)
    {
        var snapshot = db.Set<DbSnapshot>().Find(
            stream.Id,
            stream.StreamType,
            stream.TenantId,
            StateTypeName);

        var snapshotVersion = snapshot?.Version ?? 0;
        var snapshotState = snapshot is null ? default : Deserialize(snapshot);
        var tailStream = new DbStream
        {
            Id = stream.Id,
            StreamType = stream.StreamType,
            TenantId = stream.TenantId,
            CurrentVersion = stream.CurrentVersion,
            CreatedTimestamp = stream.CreatedTimestamp,
            UpdatedTimestamp = stream.UpdatedTimestamp,
            Events = stream.Events
                .Where(e => e.Version > snapshotVersion)
                .OrderBy(e => e.Version)
                .ToList()
        };

        var state = new DbContextStream<TState>(tailStream, db, snapshotState).State;
        if (snapshot is null)
        {
            snapshot = new DbSnapshot
            {
                StreamId = stream.Id,
                StreamType = stream.StreamType,
                TenantId = stream.TenantId,
                StateType = StateTypeName
            };
            db.Add(snapshot);
        }

        snapshot.Version = stream.CurrentVersion;
        snapshot.Data = JsonSerializer.Serialize(state, typeof(TState));
        snapshot.Timestamp = DateTimeOffset.UtcNow;
    }

    internal static TState? Deserialize(DbSnapshot snapshot)
        => JsonSerializer.Deserialize<TState>(snapshot.Data);
}

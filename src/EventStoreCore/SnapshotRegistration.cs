using EventStoreCore.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace EventStoreCore;

internal abstract class SnapshotRegistration
{
    protected SnapshotRegistration(
        string streamType,
        Type stateType,
        int interval,
        int schemaVersion,
        SnapshotSchemaMismatchBehavior mismatchBehavior)
    {
        StreamType = streamType;
        StateType = stateType;
        StateTypeName = stateType.FullName
            ?? throw new InvalidOperationException($"State type '{stateType.Name}' does not have a full name.");
        Interval = interval;
        SchemaVersion = schemaVersion;
        MismatchBehavior = mismatchBehavior;
    }

    public string StreamType { get; }

    public Type StateType { get; }

    public string StateTypeName { get; }

    public int Interval { get; }

    public int SchemaVersion { get; }

    public SnapshotSchemaMismatchBehavior MismatchBehavior { get; }

    public bool ShouldSnapshot(long previousVersion, long currentVersion)
        => previousVersion / Interval < currentVersion / Interval;

    public abstract void SaveSnapshot(
        DbContext db,
        DbStream stream,
        IEventStoreSerializer serializer);
}

internal sealed class SnapshotRegistration<TState> : SnapshotRegistration
    where TState : IState, new()
{
    public SnapshotRegistration(
        string streamType,
        int interval,
        int schemaVersion,
        SnapshotSchemaMismatchBehavior mismatchBehavior)
        : base(streamType, typeof(TState), interval, schemaVersion, mismatchBehavior)
    {
    }

    public override void SaveSnapshot(
        DbContext db,
        DbStream stream,
        IEventStoreSerializer serializer)
    {
        var snapshot = db.Set<DbSnapshot>().Find(
            stream.Id,
            stream.StreamType,
            stream.TenantId,
            StateTypeName);

        var schemaMatches = snapshot is null || snapshot.SchemaVersion == SchemaVersion;
        if (!schemaMatches && MismatchBehavior == SnapshotSchemaMismatchBehavior.Throw)
        {
            throw new InvalidOperationException(
                $"Snapshot schema version {snapshot!.SchemaVersion} for '{StateTypeName}' does not match configured version {SchemaVersion}.");
        }

        var snapshotVersion = schemaMatches ? snapshot?.Version ?? 0 : 0;
        var snapshotState = snapshot is not null && schemaMatches
            ? Deserialize(snapshot, serializer)
            : default;
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
        snapshot.Data = serializer.Serialize(state, typeof(TState));
        snapshot.SchemaVersion = SchemaVersion;
        snapshot.Timestamp = DateTimeOffset.UtcNow;
    }

    internal static TState? Deserialize(
        DbSnapshot snapshot,
        IEventStoreSerializer serializer)
        => (TState?)serializer.Deserialize(snapshot.Data, typeof(TState));
}

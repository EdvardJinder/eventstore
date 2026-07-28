using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace EventStoreCore;

/// <summary>
/// OpenTelemetry-compatible diagnostic source names emitted by EventStoreCore daemons.
/// </summary>
public static class EventStoreDaemonTelemetry
{
    /// <summary>The activity source name.</summary>
    public const string ActivitySourceName = "EventStoreCore.Daemons";

    /// <summary>The meter name.</summary>
    public const string MeterName = "EventStoreCore.Daemons";
}

internal static class EventStoreDaemonDiagnostics
{
    internal static readonly ActivitySource ActivitySource = new(EventStoreDaemonTelemetry.ActivitySourceName);
    private static readonly Meter Meter = new(EventStoreDaemonTelemetry.MeterName);
    private static readonly Histogram<double> BatchDuration = Meter.CreateHistogram<double>(
        "eventstore.daemon.batch.duration",
        "s",
        "Daemon batch processing duration.");
    private static readonly Counter<long> ProcessedEvents = Meter.CreateCounter<long>(
        "eventstore.daemon.events.processed");
    private static readonly Counter<long> FailedEvents = Meter.CreateCounter<long>(
        "eventstore.daemon.events.failed");
    private static readonly Counter<long> Retries = Meter.CreateCounter<long>(
        "eventstore.daemon.retries");
    private static readonly Counter<long> LockContention = Meter.CreateCounter<long>(
        "eventstore.daemon.lock.contention");
    private static readonly Histogram<long> CheckpointLag = Meter.CreateHistogram<long>(
        "eventstore.daemon.checkpoint.lag",
        "{event}");
    private static readonly UpDownCounter<long> ActiveWorkers = Meter.CreateUpDownCounter<long>(
        "eventstore.daemon.workers.active",
        "{worker}",
        "Logical checkpoint workers currently running.");
    private static readonly UpDownCounter<long> ExecutingWorkers = Meter.CreateUpDownCounter<long>(
        "eventstore.daemon.workers.executing",
        "{worker}",
        "Checkpoint workers currently processing a batch.");
    private static readonly Counter<long> ThrottledWorkers = Meter.CreateCounter<long>(
        "eventstore.daemon.workers.throttled",
        "{worker}",
        "Checkpoint worker batch attempts delayed by the daemon concurrency limit.");
    private static readonly Histogram<double> WorkerQueueDuration = Meter.CreateHistogram<double>(
        "eventstore.daemon.worker.queue.duration",
        "s",
        "Time checkpoint workers spend waiting for a daemon concurrency slot.");

    internal static Activity? StartBatch(
        string identity,
        string kind,
        CheckpointScopeKey? checkpointScope = null)
    {
        var tags = new List<KeyValuePair<string, object?>>
        {
            new("eventstore.daemon.identity", identity),
            new("eventstore.daemon.kind", kind)
        };
        if (checkpointScope is { } scope)
        {
            tags.Add(new("eventstore.daemon.checkpoint.scope", scope.Scope.ToString()));
            if (scope.IsTenant)
            {
                tags.Add(new("eventstore.daemon.checkpoint.tenant_id", scope.TenantId));
            }
        }

        return ActivitySource.StartActivity(
            "eventstore.daemon.process_batch",
            ActivityKind.Internal,
            default(ActivityContext),
            tags);
    }

    internal static void BatchCompleted(string identity, string kind, long count, TimeSpan duration, long lag = 0)
    {
        var tags = new TagList
        {
            { "eventstore.daemon.identity", identity },
            { "eventstore.daemon.kind", kind }
        };
        BatchDuration.Record(duration.TotalSeconds, tags);
        ProcessedEvents.Add(count, tags);
        CheckpointLag.Record(Math.Max(0, lag), tags);
    }

    internal static void Failed(string identity, string kind) =>
        FailedEvents.Add(1, CreateTags(identity, kind));

    internal static void Retry(string identity, string kind) =>
        Retries.Add(1, CreateTags(identity, kind));

    internal static void LockContended(string identity, string kind) =>
        LockContention.Add(1, CreateTags(identity, kind));

    internal static void WorkerStarted(string identity, string kind) =>
        ActiveWorkers.Add(1, CreateTags(identity, kind));

    internal static void WorkerStopped(string identity, string kind) =>
        ActiveWorkers.Add(-1, CreateTags(identity, kind));

    internal static void WorkerThrottled(string identity, string kind) =>
        ThrottledWorkers.Add(1, CreateTags(identity, kind));

    internal static void ExecutionStarted(
        string identity,
        string kind,
        TimeSpan queueDuration)
    {
        var tags = CreateTags(identity, kind);
        ExecutingWorkers.Add(1, tags);
        WorkerQueueDuration.Record(queueDuration.TotalSeconds, tags);
    }

    internal static void ExecutionStopped(string identity, string kind) =>
        ExecutingWorkers.Add(-1, CreateTags(identity, kind));

    private static TagList CreateTags(string identity, string kind) =>
        new()
        {
            { "eventstore.daemon.identity", identity },
            { "eventstore.daemon.kind", kind }
        };
}

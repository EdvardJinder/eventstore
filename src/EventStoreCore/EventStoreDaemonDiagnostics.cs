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

    internal static Activity? StartBatch(string identity, string kind) =>
        ActivitySource.StartActivity(
            "eventstore.daemon.process_batch",
            ActivityKind.Internal,
            default(ActivityContext),
            tags:
            new KeyValuePair<string, object?>[]
            {
                new("eventstore.daemon.identity", identity),
                new("eventstore.daemon.kind", kind)
            });

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

    private static TagList CreateTags(string identity, string kind) =>
        new()
        {
            { "eventstore.daemon.identity", identity },
            { "eventstore.daemon.kind", kind }
        };
}

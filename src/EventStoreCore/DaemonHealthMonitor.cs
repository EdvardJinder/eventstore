using System.Collections.Concurrent;

namespace EventStoreCore;

/// <summary>
/// Provides dependency-free daemon liveness and fault health checks.
/// </summary>
public sealed class DaemonHealthMonitor
{
    private readonly ConcurrentDictionary<string, DaemonHealthEntry> _entries = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates a daemon health monitor.</summary>
    /// <param name="timeProvider">Optional clock used to evaluate staleness.</param>
    public DaemonHealthMonitor(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Evaluates all observed daemons. A faulted checkpoint is unhealthy; an overdue heartbeat is degraded.
    /// </summary>
    /// <param name="stalledAfter">Maximum age of a healthy heartbeat.</param>
    /// <returns>The current daemon health report.</returns>
    public DaemonHealthReport CheckHealth(TimeSpan stalledAfter)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(stalledAfter, TimeSpan.Zero);
        var now = _timeProvider.GetUtcNow();
        var entries = _entries.Values.OrderBy(entry => entry.Identity, StringComparer.Ordinal).ToArray();
        var status = entries.Any(entry => entry.IsFaulted)
            ? DaemonHealthStatus.Unhealthy
            : entries.Any(entry => now - entry.LastHeartbeat > stalledAfter)
                ? DaemonHealthStatus.Degraded
                : DaemonHealthStatus.Healthy;
        return new DaemonHealthReport(status, entries);
    }

    internal void Heartbeat(string identity, string daemonKind) =>
        _entries.AddOrUpdate(
            $"{daemonKind}:{identity}",
            _ => new DaemonHealthEntry(identity, daemonKind, _timeProvider.GetUtcNow(), false, null),
            (_, current) => current with
            {
                LastHeartbeat = _timeProvider.GetUtcNow(),
                IsFaulted = false,
                LastError = null
            });

    internal void Fault(string identity, string daemonKind, Exception exception) =>
        _entries.AddOrUpdate(
            $"{daemonKind}:{identity}",
            _ => new DaemonHealthEntry(identity, daemonKind, _timeProvider.GetUtcNow(), true, exception.Message),
            (_, current) => current with
            {
                LastHeartbeat = _timeProvider.GetUtcNow(),
                IsFaulted = true,
                LastError = exception.Message
            });
}

/// <summary>Overall daemon health state.</summary>
public enum DaemonHealthStatus
{
    /// <summary>All observed daemons are live and non-faulted.</summary>
    Healthy,
    /// <summary>At least one daemon heartbeat is overdue.</summary>
    Degraded,
    /// <summary>At least one checkpoint is faulted or dead-lettered.</summary>
    Unhealthy
}

/// <summary>
/// Represents current health for all observed daemons.
/// </summary>
/// <param name="Status">The overall health status.</param>
/// <param name="Entries">Per-daemon health details.</param>
public sealed record DaemonHealthReport(
    DaemonHealthStatus Status,
    IReadOnlyList<DaemonHealthEntry> Entries);

/// <summary>
/// Represents liveness and fault information for one logical daemon identity.
/// </summary>
/// <param name="Identity">The stable logical identity.</param>
/// <param name="DaemonKind">The daemon kind.</param>
/// <param name="LastHeartbeat">The latest observed successful activity.</param>
/// <param name="IsFaulted">Whether the latest observed transition was a fault.</param>
/// <param name="LastError">The latest fault message.</param>
public sealed record DaemonHealthEntry(
    string Identity,
    string DaemonKind,
    DateTimeOffset LastHeartbeat,
    bool IsFaulted,
    string? LastError);

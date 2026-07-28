using EventStoreCore.Abstractions;

namespace EventStoreCore;

/// <summary>
/// Describes a durable daemon fault or dead-letter transition.
/// </summary>
/// <param name="Identity">The stable logical projection or subscription identity.</param>
/// <param name="DaemonKind">
/// The daemon kind: <c>projection</c>, <c>subscription</c>, or <c>outbox-subscription</c>.
/// </param>
/// <param name="State">The new persisted fault state.</param>
/// <param name="CheckpointScope">The checkpoint scope.</param>
/// <param name="TenantId">The tenant identifier for a tenant-scoped checkpoint.</param>
/// <param name="FailedSequence">The failed event sequence.</param>
/// <param name="Exception">The failure that caused the transition.</param>
/// <param name="OccurredAt">When the transition occurred.</param>
public sealed record DaemonFaultNotification(
    string Identity,
    string DaemonKind,
    string State,
    CheckpointScope CheckpointScope,
    Guid? TenantId,
    long? FailedSequence,
    Exception Exception,
    DateTimeOffset OccurredAt);

/// <summary>
/// Receives structured daemon fault and dead-letter notifications.
/// </summary>
public interface IDaemonFaultObserver
{
    /// <summary>Observes a persisted daemon fault transition.</summary>
    /// <param name="notification">The fault transition.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask OnFaultAsync(DaemonFaultNotification notification, CancellationToken ct);
}

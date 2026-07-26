using EventStoreCore.Abstractions;

namespace EventStoreCore;

internal sealed class DbStreamLifecycleEntry
{
    public Guid Id { get; set; }

    public Guid StreamId { get; set; }

    public string StreamType { get; set; } = string.Empty;

    public Guid TenantId { get; set; }

    public StreamLifecycleState FromState { get; set; }

    public StreamLifecycleState ToState { get; set; }

    public long StreamVersion { get; set; }

    public DateTimeOffset ChangedAtUtc { get; set; }

    public string Actor { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public string? CorrelationId { get; set; }
}

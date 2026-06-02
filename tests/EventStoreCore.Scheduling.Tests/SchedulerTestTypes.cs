using EventStoreCore.Abstractions;

namespace EventStoreCore.Tests;

internal sealed class OrderPlaced
{
    public Guid OrderId { get; init; }
}

internal sealed class PaymentCaptured
{
    public Guid OrderId { get; init; }
}

internal sealed record PaymentTimeoutArgs(Guid OrderId, Guid SourceEventId);

internal sealed class TestEvent<T>(
    Guid id,
    T data,
    Guid? streamId = null,
    DateTimeOffset? timestamp = null,
    Guid? tenantId = null) : IEvent<T>
    where T : class
{
    public Guid Id { get; } = id;

    public long Version => 1;

    public T Data { get; } = data;

    object IEvent.Data => Data;

    public Guid StreamId { get; } = streamId ?? Guid.NewGuid();

    public DateTimeOffset Timestamp { get; } = timestamp ?? DateTimeOffset.UtcNow;

    public Guid TenantId { get; } = tenantId ?? Guid.Empty;

    public Type EventType => typeof(T);
}

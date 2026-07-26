using EventStoreCore.Abstractions;

namespace EventStoreCore;

internal class OutboxEvent : IOutboxEvent
{
    internal OutboxEvent(DbOutboxMessage message, Type eventType, object data)
    {
        Id = message.EventId;
        Sequence = message.Sequence;
        Data = data;
        EventType = eventType;
        Timestamp = message.Timestamp;
        TenantId = message.TenantId;
        SourceEntityType = message.SourceEntityType;
        SourceEntityKey = message.SourceEntityKey;
        ChangeKind = message.ChangeKind;
    }

    public Guid Id { get; }

    public long Sequence { get; }

    public object Data { get; }

    public Type EventType { get; }

    public DateTimeOffset Timestamp { get; }

    public Guid TenantId { get; }

    public string SourceEntityType { get; }

    public string SourceEntityKey { get; }

    public EntityChangeKind ChangeKind { get; }
}

internal sealed class OutboxEvent<T> : OutboxEvent, IOutboxEvent<T>
    where T : class
{
    internal OutboxEvent(DbOutboxMessage message, Type eventType, object data)
        : base(message, eventType, data)
    {
        Data = (T)data;
    }

    public new T Data { get; }
}

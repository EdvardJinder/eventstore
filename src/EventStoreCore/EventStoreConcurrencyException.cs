using EventStoreCore.Abstractions;

namespace EventStoreCore;

/// <summary>
/// Exception thrown when an append operation violates optimistic concurrency expectations.
/// </summary>
public sealed class EventStoreConcurrencyException : Exception
{
    /// <summary>
    /// Creates a new concurrency exception.
    /// </summary>
    /// <param name="streamType">The stream type.</param>
    /// <param name="streamId">The stream identifier.</param>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="expectedVersion">The expected-version mode.</param>
    /// <param name="actualVersion">The observed stream version, when known.</param>
    /// <param name="message">The exception message.</param>
    /// <param name="innerException">The inner exception.</param>
    public EventStoreConcurrencyException(
        string streamType,
        Guid streamId,
        Guid tenantId,
        ExpectedVersion expectedVersion,
        long? actualVersion,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StreamType = streamType;
        StreamId = streamId;
        TenantId = tenantId;
        ExpectedVersion = expectedVersion;
        ActualVersion = actualVersion;
    }

    /// <summary>
    /// The stream type.
    /// </summary>
    public string StreamType { get; }

    /// <summary>
    /// The stream identifier.
    /// </summary>
    public Guid StreamId { get; }

    /// <summary>
    /// The tenant identifier.
    /// </summary>
    public Guid TenantId { get; }

    /// <summary>
    /// The expected-version mode.
    /// </summary>
    public ExpectedVersion ExpectedVersion { get; }

    /// <summary>
    /// The observed version at the time of failure, when known.
    /// </summary>
    public long? ActualVersion { get; }
}

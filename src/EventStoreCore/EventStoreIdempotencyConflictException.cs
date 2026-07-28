namespace EventStoreCore;

/// <summary>
/// Exception thrown when an idempotency key or event identifier is reused for a different append.
/// </summary>
public sealed class EventStoreIdempotencyConflictException : Exception
{
    /// <summary>
    /// Creates an idempotency conflict exception.
    /// </summary>
    /// <param name="message">The exception message.</param>
    /// <param name="idempotencyKey">The conflicting operation idempotency key, when applicable.</param>
    /// <param name="eventId">The conflicting event identifier, when applicable.</param>
    /// <param name="innerException">The inner exception.</param>
    public EventStoreIdempotencyConflictException(
        string message,
        Guid? idempotencyKey = null,
        Guid? eventId = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        IdempotencyKey = idempotencyKey;
        EventId = eventId;
    }

    /// <summary>
    /// The conflicting operation idempotency key, when applicable.
    /// </summary>
    public Guid? IdempotencyKey { get; }

    /// <summary>
    /// The conflicting event identifier, when applicable.
    /// </summary>
    public Guid? EventId { get; }
}

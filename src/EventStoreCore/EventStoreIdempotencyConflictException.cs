namespace EventStoreCore;

/// <summary>
/// Exception thrown when an event identifier is reused for a different append.
/// </summary>
public sealed class EventStoreIdempotencyConflictException : Exception
{
    /// <summary>
    /// Creates an idempotency conflict exception.
    /// </summary>
    /// <param name="message">The exception message.</param>
    /// <param name="eventId">The conflicting event identifier.</param>
    /// <param name="innerException">The inner exception.</param>
    public EventStoreIdempotencyConflictException(
        string message,
        Guid eventId,
        Exception? innerException = null)
        : base(message, innerException)
    {
        EventId = eventId;
    }

    /// <summary>
    /// The conflicting event identifier.
    /// </summary>
    public Guid EventId { get; }
}

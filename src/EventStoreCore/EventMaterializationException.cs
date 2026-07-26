namespace EventStoreCore;

/// <summary>
/// Exception thrown when an event cannot be materialized from persisted data.
/// </summary>
public sealed class EventMaterializationException : Exception
{
    /// <summary>
    /// Creates a new instance with the specified message.
    /// </summary>
    /// <param name="message">The exception message.</param>
    public EventMaterializationException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Creates a new instance with the specified message and inner exception.
    /// </summary>
    /// <param name="message">The exception message.</param>
    /// <param name="innerException">The inner exception.</param>
    public EventMaterializationException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    internal EventMaterializationException(string message, DbEvent? dbEvent, Exception? innerException = null)
        : base(message, innerException)
    {
        DbEvent = dbEvent;
    }

    internal DbEvent? DbEvent { get; }
}

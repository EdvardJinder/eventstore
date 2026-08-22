namespace EventStoreCore;

/// <summary>
/// Configures ordering and source selection for an inline event handler.
/// </summary>
public sealed class InlineEventHandlerRegistrationOptions
{
    /// <summary>
    /// Gets or sets the handler order. Lower values run first; registrations with the same value
    /// run in registration order.
    /// </summary>
    public int Order { get; set; }

    /// <summary>Gets or sets the event sources handled by the registration.</summary>
    public InlineEventSource Sources { get; set; } = InlineEventSource.All;
}

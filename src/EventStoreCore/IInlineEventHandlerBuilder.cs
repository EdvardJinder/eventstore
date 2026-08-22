using EventStoreCore.Abstractions;

namespace EventStoreCore;

/// <summary>
/// Registers inline event handlers for one EF Core context.
/// </summary>
public interface IInlineEventHandlerBuilder
{
    /// <summary>
    /// Gets or sets the maximum number of source envelopes dispatched by one save operation.
    /// </summary>
    int MaxDispatchCount { get; set; }

    /// <summary>Registers a handler for a strongly typed event payload.</summary>
    /// <typeparam name="THandler">The handler implementation.</typeparam>
    /// <typeparam name="TEvent">The event payload type.</typeparam>
    /// <param name="configure">Optional ordering and source configuration.</param>
    /// <returns>The builder for chaining.</returns>
    IInlineEventHandlerBuilder Add<THandler, TEvent>(
        Action<InlineEventHandlerRegistrationOptions>? configure = null)
        where THandler : class, IInlineEventHandler<TEvent>
        where TEvent : class;
}

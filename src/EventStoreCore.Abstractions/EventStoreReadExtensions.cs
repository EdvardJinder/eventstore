namespace EventStoreCore.Abstractions;

/// <summary>
/// Convenience overloads for paged and asynchronous stream reads.
/// </summary>
public static class EventStoreReadExtensions
{
    /// <summary>
    /// Reads a typed page from the complete stream identity.
    /// </summary>
    /// <typeparam name="TEvent">The payload contract required for every event in the page.</typeparam>
    public static async Task<StreamPage<TEvent>?> ReadPageAsync<TEvent>(
        this IEventStore eventStore,
        string streamType,
        Guid streamId,
        Guid tenantId,
        StreamReadOptions options,
        CancellationToken cancellationToken = default)
        where TEvent : class
    {
        var page = await eventStore.ReadPageAsync(
            streamType,
            streamId,
            tenantId,
            options,
            cancellationToken);
        if (page is null)
        {
            return null;
        }

        return new StreamPage<TEvent>(
            page.Events.Select(RequireTyped<TEvent>).ToArray(),
            page.StreamVersion,
            page.NextVersion);
    }

    /// <summary>
    /// Asynchronously enumerates typed events from the complete stream identity.
    /// </summary>
    /// <typeparam name="TEvent">The payload contract required for every event in the range.</typeparam>
    public static async IAsyncEnumerable<IEvent<TEvent>> ReadAsync<TEvent>(
        this IEventStore eventStore,
        string streamType,
        Guid streamId,
        Guid tenantId,
        StreamReadOptions options,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken = default)
        where TEvent : class
    {
        await foreach (var @event in eventStore.ReadAsync(
            streamType,
            streamId,
            tenantId,
            options,
            cancellationToken))
        {
            yield return RequireTyped<TEvent>(@event);
        }
    }

    /// <summary>
    /// Reads a page from the default stream type and tenant.
    /// </summary>
    public static Task<StreamPage?> ReadPageAsync(
        this IEventStore eventStore,
        Guid streamId,
        StreamReadOptions options,
        CancellationToken cancellationToken = default)
        => eventStore.ReadPageAsync(string.Empty, streamId, Guid.Empty, options, cancellationToken);

    /// <summary>
    /// Reads a page from the specified stream type and default tenant.
    /// </summary>
    public static Task<StreamPage?> ReadPageAsync(
        this IEventStore eventStore,
        string streamType,
        Guid streamId,
        StreamReadOptions options,
        CancellationToken cancellationToken = default)
        => eventStore.ReadPageAsync(streamType, streamId, Guid.Empty, options, cancellationToken);

    /// <summary>
    /// Reads a page from the default stream type and specified tenant.
    /// </summary>
    public static Task<StreamPage?> ReadPageAsync(
        this IEventStore eventStore,
        Guid streamId,
        Guid tenantId,
        StreamReadOptions options,
        CancellationToken cancellationToken = default)
        => eventStore.ReadPageAsync(string.Empty, streamId, tenantId, options, cancellationToken);

    /// <summary>
    /// Enumerates a range from the default stream type and tenant.
    /// </summary>
    public static IAsyncEnumerable<IEvent> ReadAsync(
        this IEventStore eventStore,
        Guid streamId,
        StreamReadOptions options,
        CancellationToken cancellationToken = default)
        => eventStore.ReadAsync(string.Empty, streamId, Guid.Empty, options, cancellationToken);

    /// <summary>
    /// Enumerates a range from the specified stream type and default tenant.
    /// </summary>
    public static IAsyncEnumerable<IEvent> ReadAsync(
        this IEventStore eventStore,
        string streamType,
        Guid streamId,
        StreamReadOptions options,
        CancellationToken cancellationToken = default)
        => eventStore.ReadAsync(streamType, streamId, Guid.Empty, options, cancellationToken);

    /// <summary>
    /// Enumerates a range from the default stream type and specified tenant.
    /// </summary>
    public static IAsyncEnumerable<IEvent> ReadAsync(
        this IEventStore eventStore,
        Guid streamId,
        Guid tenantId,
        StreamReadOptions options,
        CancellationToken cancellationToken = default)
        => eventStore.ReadAsync(string.Empty, streamId, tenantId, options, cancellationToken);

    private static IEvent<TEvent> RequireTyped<TEvent>(IEvent @event)
        where TEvent : class
        => @event as IEvent<TEvent>
            ?? throw new InvalidOperationException(
                $"Event at stream version {@event.Version} has payload type '{@event.EventType.FullName ?? @event.EventType.Name}', which is not assignable to '{typeof(TEvent).FullName ?? typeof(TEvent).Name}'.");
}

namespace EventStoreCore.Scheduling;

/// <summary>
/// Executes scheduler operations for a concrete scheduler provider.
/// </summary>
public interface ISchedulerExecutionAdapter
{
    /// <summary>
    /// Schedules or replaces delayed work for the specified key and source event.
    /// </summary>
    /// <typeparam name="TArgs">The scheduled job payload type.</typeparam>
    /// <param name="key">The stable schedule key.</param>
    /// <param name="sourceEventId">The event id that requested the schedule.</param>
    /// <param name="delay">The delay before execution.</param>
    /// <param name="args">The scheduled job payload.</param>
    /// <param name="ct">Cancellation token.</param>
    Task ScheduleAsync<TArgs>(
        ScheduleKey key,
        Guid sourceEventId,
        TimeSpan delay,
        TArgs args,
        CancellationToken ct)
        where TArgs : class;

    /// <summary>
    /// Cancels any scheduled work for the specified key.
    /// </summary>
    /// <param name="key">The stable schedule key.</param>
    /// <param name="ct">Cancellation token.</param>
    Task CancelAsync(ScheduleKey key, CancellationToken ct);
}

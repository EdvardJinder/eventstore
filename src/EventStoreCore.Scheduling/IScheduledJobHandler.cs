namespace EventStoreCore.Scheduling;

/// <summary>
/// Handles a scheduled job payload resolved by a scheduler provider.
/// </summary>
/// <typeparam name="TArgs">The scheduled job payload type.</typeparam>
public interface IScheduledJobHandler<in TArgs>
    where TArgs : class
{
    /// <summary>
    /// Handles a scheduled job payload.
    /// </summary>
    /// <param name="args">The scheduled job payload.</param>
    /// <param name="ct">Cancellation token.</param>
    Task HandleAsync(TArgs args, CancellationToken ct);
}

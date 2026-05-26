namespace EventStoreCore.Scheduling;

/// <summary>
/// Describes the scheduler provider selected for an EventStore registration.
/// </summary>
/// <param name="ProviderName">The unique scheduler provider name.</param>
internal sealed record SchedulerProviderRegistration(string ProviderName);

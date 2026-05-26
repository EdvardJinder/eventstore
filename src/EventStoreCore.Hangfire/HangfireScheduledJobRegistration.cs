namespace EventStoreCore.Hangfire;

internal sealed record HangfireScheduledJobRegistration(string JobId, Guid SourceEventId);

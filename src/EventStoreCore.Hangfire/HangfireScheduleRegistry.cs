using EventStoreCore.Scheduling;
using Hangfire;
using Hangfire.Storage;

namespace EventStoreCore.Hangfire;

internal sealed class HangfireScheduleRegistry(JobStorage storage)
{
    private const string JobIdField = "job-id";
    private const string SourceEventIdField = "source-event-id";

    public HangfireScheduledJobRegistration? Get(ScheduleKey key)
    {
        using var connection = storage.GetConnection();
        var entries = connection.GetAllEntriesFromHash(GetHashKey(key));

        if (entries is null ||
            !entries.TryGetValue(JobIdField, out var jobId) ||
            string.IsNullOrWhiteSpace(jobId) ||
            !entries.TryGetValue(SourceEventIdField, out var sourceEventIdValue) ||
            !Guid.TryParse(sourceEventIdValue, out var sourceEventId))
        {
            return null;
        }

        return new HangfireScheduledJobRegistration(jobId, sourceEventId);
    }

    public void Save(ScheduleKey key, string jobId, Guid sourceEventId)
    {
        using var connection = storage.GetConnection();
        using var transaction = connection.CreateWriteTransaction();
        transaction.SetRangeInHash(
            GetHashKey(key),
            [
                new KeyValuePair<string, string>(JobIdField, jobId),
                new KeyValuePair<string, string>(SourceEventIdField, sourceEventId.ToString("D"))
            ]);
        transaction.Commit();
    }

    public void Remove(ScheduleKey key)
    {
        using var connection = storage.GetConnection();
        using var transaction = connection.CreateWriteTransaction();
        transaction.RemoveHash(GetHashKey(key));
        transaction.Commit();
    }

    public bool JobExists(string jobId)
    {
        using var connection = storage.GetConnection();
        return connection.GetJobData(jobId) is not null;
    }

    private static string GetHashKey(ScheduleKey key) => $"eventstore:scheduler:hangfire:{key.Value}";
}

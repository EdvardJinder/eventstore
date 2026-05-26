using EventStoreCore.Scheduling;
using System.Linq.Expressions;
using System.Text.Json;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;

namespace EventStoreCore.TickerQ;

internal sealed class TickerQScheduleService(
    ITickerClock clock,
    ITimeTickerManager<TimeTickerEntity> timeTickerManager,
    ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity> persistenceProvider)
    : ISchedulerExecutionAdapter
{
    public async Task ScheduleAsync<TArgs>(
        ScheduleKey key,
        Guid sourceEventId,
        TimeSpan delay,
        TArgs args,
        CancellationToken ct)
        where TArgs : class
    {
        var existingTickers = await GetExistingTickersAsync(key, ct);
        if (existingTickers.Length == 1)
        {
            var existingEnvelope = await ReadEnvelopeAsync(existingTickers[0].Id, ct);
            if (existingEnvelope is not null && existingEnvelope.SourceEventId == sourceEventId)
            {
                return;
            }
        }

        if (existingTickers.Length > 0)
        {
            await timeTickerManager.DeleteBatchAsync(existingTickers.Select(t => t.Id).ToList(), ct);
        }

        var entity = new TimeTickerEntity
        {
            Description = key.Value,
            ExecutionTime = clock.UtcNow.Add(delay),
            Function = TickerQConstants.FunctionName,
            Request = TickerHelper.CreateTickerRequest(new TickerQScheduledEnvelope(
                SourceEventId: sourceEventId,
                ArgumentType: typeof(TArgs).AssemblyQualifiedName
                    ?? throw new InvalidOperationException($"Unable to resolve assembly-qualified name for scheduled argument type '{typeof(TArgs).FullName}'."),
                PayloadJson: JsonSerializer.Serialize(args)))
        };

        await timeTickerManager.AddAsync(entity, ct);
    }

    public async Task CancelAsync(ScheduleKey key, CancellationToken ct)
    {
        var existingTickers = await GetExistingTickersAsync(key, ct);
        if (existingTickers.Length == 0)
        {
            return;
        }

        await timeTickerManager.DeleteBatchAsync(existingTickers.Select(t => t.Id).ToList(), ct);
    }

    private Task<TimeTickerEntity[]> GetExistingTickersAsync(ScheduleKey key, CancellationToken ct)
    {
        Expression<Func<TimeTickerEntity, bool>> predicate =
            ticker => ticker.Function == TickerQConstants.FunctionName && ticker.Description == key.Value;

        return persistenceProvider.GetTimeTickers(predicate, ct);
    }

    private async Task<TickerQScheduledEnvelope?> ReadEnvelopeAsync(Guid tickerId, CancellationToken ct)
    {
        var request = await persistenceProvider.GetTimeTickerRequest(tickerId, ct);
        if (request is null || request.Length == 0)
        {
            return null;
        }

        return TickerHelper.ReadTickerRequest<TickerQScheduledEnvelope>(request);
    }
}

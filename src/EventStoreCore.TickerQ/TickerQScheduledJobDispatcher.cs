using EventStoreCore.Scheduling;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TickerQ.Utilities;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;

namespace EventStoreCore.TickerQ;

internal sealed class TickerQScheduledJobDispatcher(
    IServiceProvider serviceProvider,
    IOptions<SchedulerOptions> schedulerOptions,
    ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity> persistenceProvider)
{
    public async Task ExecuteAsync(TickerFunctionContext context, CancellationToken ct)
    {
        await DispatchAsync(context.Id, ct);
    }

    internal async Task DispatchAsync(Guid tickerId, CancellationToken ct)
    {
        var request = await persistenceProvider.GetTimeTickerRequest(tickerId, ct);
        if (request is null || request.Length == 0)
        {
            throw new InvalidOperationException($"TickerQ request payload is missing for scheduled job '{tickerId}'.");
        }

        var envelope = TickerHelper.ReadTickerRequest<TickerQScheduledEnvelope>(request);
        if (!schedulerOptions.Value.PayloadTypes.TryGetValue(envelope.ArgumentType, out var argumentType))
        {
            throw new InvalidOperationException($"Scheduled argument type '{envelope.ArgumentType}' is not registered.");
        }

        var payload = JsonSerializer.Deserialize(envelope.PayloadJson, argumentType)
            ?? throw new InvalidOperationException($"Scheduled payload for '{argumentType.FullName}' could not be deserialized.");

        var handlerType = typeof(IScheduledJobHandler<>).MakeGenericType(argumentType);
        var handleMethod = handlerType.GetMethod(nameof(IScheduledJobHandler<object>.HandleAsync))
            ?? throw new InvalidOperationException($"Scheduled job handler method could not be found for '{argumentType.FullName}'.");
        var handler = serviceProvider.GetRequiredService(handlerType);
        var task = (Task?)handleMethod.Invoke(handler, [payload, ct]);
        if (task is null)
        {
            throw new InvalidOperationException($"Scheduled job handler returned null for '{argumentType.FullName}'.");
        }

        await task;
    }
}

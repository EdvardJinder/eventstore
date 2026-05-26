namespace EventStoreCore.TickerQ;

internal sealed record TickerQScheduledEnvelope(
    Guid SourceEventId,
    string ArgumentType,
    string PayloadJson);

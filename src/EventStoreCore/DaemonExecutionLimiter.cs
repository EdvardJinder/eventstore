namespace EventStoreCore;

internal sealed class DaemonExecutionLimiter
{
    private readonly SemaphoreSlim _semaphore;
    private readonly TimeProvider _timeProvider;

    internal DaemonExecutionLimiter(int maxConcurrency, TimeProvider timeProvider)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxConcurrency, 1);
        _semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        _timeProvider = timeProvider;
    }

    internal async Task<TResult> RunAsync<TResult>(
        string identity,
        string kind,
        Func<CancellationToken, Task<TResult>> action,
        CancellationToken ct)
    {
        var queuedAt = _timeProvider.GetTimestamp();
        if (!await _semaphore.WaitAsync(0, ct))
        {
            EventStoreDaemonDiagnostics.WorkerThrottled(identity, kind);
            await _semaphore.WaitAsync(ct);
        }

        EventStoreDaemonDiagnostics.ExecutionStarted(
            identity,
            kind,
            _timeProvider.GetElapsedTime(queuedAt));
        try
        {
            return await action(ct);
        }
        finally
        {
            EventStoreDaemonDiagnostics.ExecutionStopped(identity, kind);
            _semaphore.Release();
        }
    }
}

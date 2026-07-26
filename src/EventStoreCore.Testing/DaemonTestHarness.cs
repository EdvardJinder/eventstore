using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;

namespace EventStoreCore.Testing;

/// <summary>
/// Runs a hosted daemon against a controllable clock and waits for application-observable outcomes.
/// </summary>
public sealed class DaemonTestHarness : IAsyncDisposable
{
    private readonly IHostedService _daemon;
    private bool _started;
    private bool _stopped;
    private bool _disposed;

    /// <summary>
    /// Creates a daemon test harness.
    /// </summary>
    /// <param name="daemon">The hosted daemon to run.</param>
    /// <param name="timeProvider">The controllable clock supplied to the daemon.</param>
    /// <remarks>
    /// The same <paramref name="timeProvider"/> instance must be supplied to the daemon for advancing this harness's
    /// clock to release daemon delays.
    /// </remarks>
    public DaemonTestHarness(IHostedService daemon, FakeTimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(daemon);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _daemon = daemon;
        TimeProvider = timeProvider;
    }

    /// <summary>
    /// Gets the controllable clock used by the harness.
    /// </summary>
    public FakeTimeProvider TimeProvider { get; }

    /// <summary>
    /// Gets whether the daemon has been started and has not yet been stopped.
    /// </summary>
    public bool IsRunning => _started && !_stopped;

    /// <summary>
    /// Starts the hosted daemon.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the daemon's start operation completes.</returns>
    public async Task StartAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();

        if (_started)
        {
            if (_stopped)
            {
                throw new InvalidOperationException("A stopped daemon cannot be restarted by this harness.");
            }

            return;
        }

        await _daemon.StartAsync(ct).ConfigureAwait(false);
        _started = true;
        await ThrowIfDaemonFaultedAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Advances the controllable clock and yields so daemon continuations can run.
    /// </summary>
    /// <param name="amount">The non-negative amount of simulated time to advance.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes after ready daemon continuations have been given an opportunity to run.</returns>
    public async Task AdvanceAsync(TimeSpan amount, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        EnsureRunning();

        if (amount < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "The amount to advance must be non-negative.");
        }

        ct.ThrowIfCancellationRequested();
        TimeProvider.Advance(amount);
        await Task.Yield();
        ct.ThrowIfCancellationRequested();
        await ThrowIfDaemonFaultedAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Advances time until an asynchronous application predicate succeeds.
    /// </summary>
    /// <param name="predicate">The application-observable completion predicate.</param>
    /// <param name="advanceBy">The non-negative amount of simulated time to advance between attempts.</param>
    /// <param name="maxAttempts">The maximum number of predicate evaluations. Must be greater than zero.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the predicate returns <see langword="true"/>.</returns>
    /// <exception cref="TimeoutException">
    /// Thrown when the predicate does not succeed within <paramref name="maxAttempts"/> evaluations.
    /// </exception>
    public async Task RunUntilAsync(
        Func<CancellationToken, Task<bool>> predicate,
        TimeSpan advanceBy,
        int maxAttempts = 100,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ThrowIfDisposed();
        EnsureRunning();

        if (advanceBy < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(advanceBy), "The amount to advance must be non-negative.");
        }

        if (maxAttempts <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), "The maximum attempts must be greater than zero.");
        }

        var startedAt = TimeProvider.GetUtcNow();

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            await ThrowIfDaemonFaultedAsync().ConfigureAwait(false);

            if (await predicate(ct).ConfigureAwait(false))
            {
                return;
            }

            if (attempt < maxAttempts)
            {
                await AdvanceAsync(advanceBy, ct).ConfigureAwait(false);
            }
        }

        var elapsed = TimeProvider.GetUtcNow() - startedAt;
        throw new TimeoutException(
            $"The daemon condition was not satisfied after {maxAttempts} attempts " +
            $"and {elapsed} of simulated time for '{_daemon.GetType().FullName}'.");
    }

    /// <summary>
    /// Stops the hosted daemon.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the daemon has stopped.</returns>
    public async Task StopAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();

        if (!_started || _stopped)
        {
            return;
        }

        try
        {
            await _daemon.StopAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _stopped = true;
        }
    }

    /// <summary>
    /// Stops the hosted daemon and releases the harness.
    /// </summary>
    /// <returns>A value task that completes when the daemon has stopped.</returns>
    /// <remarks>
    /// The harness does not dispose the hosted-service instance because its owning service provider may manage that
    /// lifetime.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            if (_started && !_stopped)
            {
                await _daemon.StopAsync(CancellationToken.None).ConfigureAwait(false);
                _stopped = true;
            }
        }
        finally
        {
            _disposed = true;
        }
    }

    private void EnsureRunning()
    {
        if (!IsRunning)
        {
            throw new InvalidOperationException("Start the daemon before advancing time or waiting for a condition.");
        }
    }

    private async Task ThrowIfDaemonFaultedAsync()
    {
        if (_daemon is BackgroundService { ExecuteTask: { IsFaulted: true } executeTask })
        {
            await executeTask.ConfigureAwait(false);
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}

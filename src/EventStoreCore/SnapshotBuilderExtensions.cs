using EventStoreCore.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace EventStoreCore;

/// <summary>
/// Extension methods for configuring aggregate snapshots.
/// </summary>
public static class SnapshotBuilderExtensions
{
    /// <summary>
    /// Enables aggregate snapshots for configured stream types.
    /// Configured snapshots are written during appends that cross the configured interval,
    /// and typed reads for registered state types rebuild state from the snapshot plus later events.
    /// </summary>
    /// <param name="builder">The event store builder.</param>
    /// <param name="configure">Snapshot configuration.</param>
    /// <returns>The event store builder for chaining.</returns>
    public static IEventStoreBuilder UseSnapshots(
        this IEventStoreBuilder builder,
        Action<ISnapshotBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        configure(new SnapshotBuilder(builder.Services));
        return builder;
    }

    private sealed class SnapshotBuilder(IServiceCollection services) : ISnapshotBuilder
    {
        public ISnapshotBuilder For<TState>(Action<SnapshotOptions>? configure = null)
            where TState : IState, new()
            => For<TState>(string.Empty, configure);

        public ISnapshotBuilder For<TState>(string streamType, Action<SnapshotOptions>? configure = null)
            where TState : IState, new()
        {
            ArgumentNullException.ThrowIfNull(streamType);

            var options = new SnapshotOptions();
            configure?.Invoke(options);

            if (options.Interval <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    options.Interval,
                    "Snapshot interval must be greater than zero.");
            }

            if (options.SchemaVersion <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    options.SchemaVersion,
                    "Snapshot schema version must be greater than zero.");
            }

            if (!Enum.IsDefined(options.OnSchemaMismatch))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    options.OnSchemaMismatch,
                    "Snapshot schema mismatch behavior is not supported.");
            }

            services.AddSingleton<SnapshotRegistration>(
                new SnapshotRegistration<TState>(
                    streamType,
                    options.Interval,
                    options.SchemaVersion,
                    options.OnSchemaMismatch));

            return this;
        }
    }
}

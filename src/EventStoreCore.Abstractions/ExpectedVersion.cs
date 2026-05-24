namespace EventStoreCore.Abstractions;

/// <summary>
/// Describes the expected stream version to enforce during append operations.
/// </summary>
public readonly record struct ExpectedVersion
{
    private ExpectedVersion(ExpectedVersionMode mode, long? version = null)
    {
        Mode = mode;
        Version = version;
    }

    /// <summary>
    /// The expected-version mode.
    /// </summary>
    public ExpectedVersionMode Mode { get; }

    /// <summary>
    /// The exact stream version to require when <see cref="Mode" /> is <see cref="ExpectedVersionMode.Exact" />.
    /// </summary>
    public long? Version { get; }

    /// <summary>
    /// Appends regardless of the current stream version.
    /// </summary>
    public static ExpectedVersion Any { get; } = new(ExpectedVersionMode.Any);

    /// <summary>
    /// Requires that the stream does not already exist.
    /// </summary>
    public static ExpectedVersion NoStream { get; } = new(ExpectedVersionMode.NoStream);

    /// <summary>
    /// Requires that the stream already exists, regardless of its current version.
    /// </summary>
    public static ExpectedVersion StreamExists { get; } = new(ExpectedVersionMode.StreamExists);

    /// <summary>
    /// Requires that the stream exists and is currently at the supplied version.
    /// </summary>
    /// <param name="version">The exact stream version to require.</param>
    /// <returns>The expected-version constraint.</returns>
    public static ExpectedVersion Exact(long version)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(version);
        return new ExpectedVersion(ExpectedVersionMode.Exact, version);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Mode == ExpectedVersionMode.Exact
            ? $"Exact({Version})"
            : Mode.ToString();
    }
}

/// <summary>
/// Identifies the optimistic concurrency mode used for append operations.
/// </summary>
public enum ExpectedVersionMode
{
    /// <summary>
    /// Appends regardless of the current stream version.
    /// </summary>
    Any,

    /// <summary>
    /// Requires that the stream does not already exist.
    /// </summary>
    NoStream,

    /// <summary>
    /// Requires that the stream already exists.
    /// </summary>
    StreamExists,

    /// <summary>
    /// Requires that the stream exists and matches an exact version.
    /// </summary>
    Exact
}

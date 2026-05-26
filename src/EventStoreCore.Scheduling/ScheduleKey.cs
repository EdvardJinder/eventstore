namespace EventStoreCore.Scheduling;

/// <summary>
/// Represents a stable scheduler identity used for deduplication, cancellation, and replacement.
/// </summary>
public readonly record struct ScheduleKey
{
    private ScheduleKey(string value)
    {
        Value = value;
    }

    /// <summary>
    /// Gets the scheduler key value.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Creates a validated schedule key.
    /// </summary>
    /// <param name="value">The schedule key value.</param>
    /// <returns>The validated schedule key.</returns>
    /// <exception cref="ArgumentException">Thrown when the value is null, empty, or whitespace.</exception>
    public static ScheduleKey Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Schedule keys must not be null, empty, or whitespace.", nameof(value));
        }

        return new ScheduleKey(value);
    }

    /// <summary>
    /// Returns the schedule key value.
    /// </summary>
    /// <returns>The underlying string value.</returns>
    public override string ToString() => Value;
}

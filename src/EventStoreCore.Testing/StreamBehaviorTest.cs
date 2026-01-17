using EventStoreCore.Abstractions;
using EventStoreCore;

using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace EventStoreCore.Testing;

/// <summary>
/// Base class for behavior-style tests of event-sourced streams.
/// Provides Given/When/Then helpers for asserting emitted events and state.
/// </summary>
/// <typeparam name="TState">State type rebuilt by applying stream events.</typeparam>
public abstract class StreamBehaviorTest<TState> : IDisposable
    where TState : IState, new()
{
    private readonly DbContext _dbContext;
    private readonly IStream<TState> _stream;
    private long LastVersion { get; set; } = 0;

    /// <summary>
    /// Maximum allowed difference (in milliseconds) for date comparisons when asserting events.
    /// Override to tolerate clock or serialization differences.
    /// </summary>
    protected virtual int MaxMillisecondsDateDifference => 0;

    /// <summary>
    /// Creates a new in-memory test fixture with an empty stream.
    /// </summary>
    protected StreamBehaviorTest()
    {
        _dbContext = new TestDbContext(Guid.NewGuid().ToString("N"));
        _stream = new DbContextStream<TState>(new DbStream
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            CreatedTimestamp = DateTimeOffset.UtcNow,
            UpdatedTimestamp = DateTimeOffset.UtcNow,
            CurrentVersion = 0
        });

    }


    /// <summary>
    /// Seeds the stream with historical events that should not be asserted in <see cref="Then"/>.
    /// </summary>
    /// <param name="events">Events to append to the stream before the test action.</param>
    protected void Given(params object[] events)
    {
        _stream.Append(events);
        _dbContext.SaveChanges();
        LastVersion = _stream.Version;
    }

    /// <summary>
    /// Executes the test action that appends events to the stream.
    /// </summary>
    /// <param name="act">Action that appends events to the stream.</param>
    protected void When(Action<IStream<TState>> act)
    {
        act(_stream);
        _dbContext.SaveChanges();
    }

    /// <summary>
    /// Executes a test action and asserts it throws the expected exception type.
    /// </summary>
    /// <typeparam name="TException">Expected exception type.</typeparam>
    /// <param name="act">Action that is expected to throw.</param>
    /// <returns>The thrown exception for further assertions.</returns>
    protected TException Throws<TException>(Action<IStream<TState>> act)
        where TException : Exception
    {
        var exception = Should.Throw<TException>(() =>
        {
            act(_stream);
            _dbContext.SaveChanges();
        });
        return exception;
    }

    /// <summary>
    /// Asserts that the events appended after <see cref="Given"/> match the expected events, in order.
    /// </summary>
    /// <param name="expectedEvents">Expected events appended by the test action.</param>
    protected void Then(params object[] expectedEvents)
    {
        var config = new KellermanSoftware.CompareNetObjects.ComparisonConfig
        {
            IgnoreCollectionOrder = false,
            MaxDifferences = 100,
            MaxMillisecondsDateDifference = MaxMillisecondsDateDifference
        };
        var compareLogic = new KellermanSoftware.CompareNetObjects.CompareLogic(config);
        var committed = _stream.Events.Where(e => e.Version > LastVersion).ToList();
        var actualEvents = committed.Select(x => x.Data).ToArray();
        var result = compareLogic.Compare(expectedEvents, actualEvents);
        if (!result.AreEqual)
        {
            var diffMessage = BuildDiffMessage(expectedEvents, actualEvents, result.DifferencesString);
            result.AreEqual.ShouldBeTrue(diffMessage);
        }
    }

    /// <summary>
    /// Asserts against the current state rebuilt from all stream events.
    /// </summary>
    /// <param name="assert">Assertion action on the current state.</param>
    protected void ThenState(Action<TState> assert)
    {
        assert(_stream.State);
    }


    /// <summary>
    /// Builds a detailed diff message for mismatched event lists.
    /// </summary>
    private static string BuildDiffMessage(object[] expectedEvents, object[] actualEvents, string? differences)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Event mismatch.");
        sb.AppendLine($"Expected count: {expectedEvents.Length}, Actual count: {actualEvents.Length}");
        if (!string.IsNullOrWhiteSpace(differences))
        {
            sb.AppendLine("Differences:");
            sb.AppendLine(differences.Trim());
        }
        sb.AppendLine("Expected:");
        AppendEventList(sb, expectedEvents);
        sb.AppendLine("Actual:");
        AppendEventList(sb, actualEvents);
        return sb.ToString();
    }

    /// <summary>
    /// Appends a formatted list of events to the supplied builder.
    /// </summary>
    private static void AppendEventList(System.Text.StringBuilder sb, object[] events)
    {
        for (var i = 0; i < events.Length; i++)
        {
            sb.Append('[').Append(i).Append("] ").AppendLine(FormatEvent(events[i]));
        }
        if (events.Length == 0)
        {
            sb.AppendLine("<none>");
        }
    }

    /// <summary>
    /// Formats an event for diagnostic output.
    /// </summary>
    private static string FormatEvent(object? @event)
    {
        if (@event is null)
        {
            return "<null>";
        }
        var typeName = @event.GetType().FullName ?? @event.GetType().Name;
        var json = System.Text.Json.JsonSerializer.Serialize(@event);
        return $"{typeName}: {json}";
    }

    /// <summary>
    /// Disposes the underlying DbContext.
    /// </summary>
    public void Dispose() => _dbContext.Dispose();
}


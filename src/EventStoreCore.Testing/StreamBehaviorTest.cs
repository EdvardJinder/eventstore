using EventStoreCore.Abstractions;
using EventStoreCore.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace EventStoreCore.Testing;

public abstract class StreamBehaviorTest<TState> : IDisposable
    where TState : IState, new()
{
    private readonly DbContext _dbContext;
    private readonly IStream<TState> _stream;
    private long LastVersion { get; set; } = 0;
    protected virtual int MaxMillisecondsDateDifference => 0;

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
        }, _dbContext);
    }

    protected void Given(params object[] events)
    {
        _stream.Append(events);
        _dbContext.SaveChanges();
        LastVersion = _stream.Version;
    }

    protected void When(Action<IStream<TState>> act)
    {
        act(_stream);
        _dbContext.SaveChanges();
    }

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

    protected void ThenState(Action<TState> assert)
    {
        assert(_stream.State);
    }

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

    public void Dispose() => _dbContext.Dispose();
}

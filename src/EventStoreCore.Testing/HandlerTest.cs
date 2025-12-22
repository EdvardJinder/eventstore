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

    protected void Then(params object[] expectedEvents)
    {
        var config = new KellermanSoftware.CompareNetObjects.ComparisonConfig
        {
            IgnoreCollectionOrder = false,
            MaxDifferences = 100,
            MaxMillisecondsDateDifference = 0
        };
        var compareLogic = new KellermanSoftware.CompareNetObjects.CompareLogic(config);
        var committed = _stream.Events.Where(e => e.Version > LastVersion).ToList();
        var result = compareLogic.Compare(expectedEvents, committed.Select(x => x.Data).ToArray());
        result.AreEqual.ShouldBeTrue(result.DifferencesString);
    }

    public void Dispose() => _dbContext.Dispose();
}

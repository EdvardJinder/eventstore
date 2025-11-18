using IM.EventStore.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace IM.EventStore.Testing;

public abstract class HandlerTest<THandler, TCommand>
    where THandler : IHandler<TCommand>
    where TCommand : class
{
    protected TimeProvider TimeProvider { get; set; } = new FakeTimeProvider(DateTimeOffset.UtcNow);
    private List<object> _committedEvents = new();
    protected HandlerTest()
    {
    }
    protected void Given(params object[] events)
    {
        _committedEvents.AddRange(events);
    }
    protected void When(TCommand command)
    {
        _committedEvents.AddRange(THandler.Handle(command));
    }
    protected void Then(params ICollection<object> expectedEvents)
    {
        var config = new KellermanSoftware.CompareNetObjects.ComparisonConfig
        {
            IgnoreCollectionOrder = true,   // ignore order
            MaxDifferences = 100,
            MaxMillisecondsDateDifference = 0
        };
        var compareLogic = new KellermanSoftware.CompareNetObjects.CompareLogic(config);
        var result = compareLogic.Compare(expectedEvents.ToArray(), _committedEvents.ToArray());
        result.AreEqual.ShouldBeTrue(result.DifferencesString);
    }
    protected void ThrowsWhen<TException>(TCommand command) where TException : Exception
    {
        Should.Throw<TException>(() => When(command));
    }
}

public abstract class HandlerTest<THandler, TState, TCommand>
    where THandler : IHandler<TState, TCommand>
    where TState : IState, new()
    where TCommand : class
{
    protected TimeProvider TimeProvider { get; set; } = new FakeTimeProvider(DateTimeOffset.UtcNow);

    private readonly DbContext _dbContext = new TestDbContext();

    private IStream<TState> _stream;
    private long LastVersion { get; set; } = 0;

    protected HandlerTest()
    {
        _stream = new Stream<TState>(new DbStream()
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            CreatedTimestamp = TimeProvider.GetUtcNow(),
            UpdatedTimestamp = TimeProvider.GetUtcNow(),
            CurrentVersion = 0
        }, _dbContext); 
    }

    protected void Given(params object[] events)
    {
        _stream.Append(events);
        LastVersion = _stream.Version;
    }

    protected void When(TCommand command)
    {
        THandler.Handle(_stream, command);
        
    }

    protected void Then(params ICollection<object> expectedEvents)
    {
        var config = new KellermanSoftware.CompareNetObjects.ComparisonConfig
        {
            IgnoreCollectionOrder = true,   // ignore order
            MaxDifferences = 100,
            MaxMillisecondsDateDifference = 0
        };
        var compareLogic = new KellermanSoftware.CompareNetObjects.CompareLogic(config);
        var committed = _stream.Events.Where(e => e.Version > LastVersion).ToList();
        var result = compareLogic.Compare(expectedEvents.ToArray(), committed.Select(x => x.Data).ToArray());
        result.AreEqual.ShouldBeTrue(result.DifferencesString);
    }

    protected void ThrowsWhen<TException>(TCommand command) where TException : Exception
    {
        Should.Throw<TException>(() => When(command));
    }

}

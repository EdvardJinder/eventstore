using EventStoreCore.Abstractions;

namespace EventStoreCore.Tests;

public sealed class StreamApiContractTests
{
    [Fact]
    public void Typed_read_stream_is_substitutable_for_untyped_read_stream()
    {
        IReadOnlyStream<TestState> typed = CreateTypedStream();
        IReadOnlyStream untyped = typed;

        Assert.Same(typed, untyped);
        Assert.Equal(typed.Id, untyped.Id);
        Assert.Equal(typed.StreamType, untyped.StreamType);
        Assert.Equal(typed.TenantId, untyped.TenantId);
    }

    [Fact]
    public void Typed_write_stream_is_substitutable_for_untyped_write_stream()
    {
        IStream<TestState> typed = CreateTypedStream();
        IStream untyped = typed;

        untyped.Append(new TestEvent());

        Assert.Single(untyped.Events);
        Assert.Equal(1, untyped.Version);
    }

    [Fact]
    public void Generic_stream_interfaces_only_declare_typed_state()
    {
        var readMembers = typeof(IReadOnlyStream<TestState>)
            .GetMembers()
            .Where(member => member.DeclaringType == typeof(IReadOnlyStream<TestState>))
            .Select(member => member.Name)
            .ToArray();
        var writeMembers = typeof(IStream<TestState>)
            .GetMembers()
            .Where(member => member.DeclaringType == typeof(IStream<TestState>))
            .Select(member => member.Name)
            .ToArray();

        Assert.Contains(nameof(IReadOnlyStream<TestState>.State), readMembers);
        Assert.DoesNotContain(nameof(IReadOnlyStream.Id), readMembers);
        Assert.DoesNotContain(nameof(IReadOnlyStream.StreamType), readMembers);
        Assert.DoesNotContain(nameof(IReadOnlyStream.TenantId), readMembers);
        Assert.DoesNotContain(nameof(IReadOnlyStream.Version), readMembers);
        Assert.DoesNotContain(nameof(IReadOnlyStream.Events), readMembers);
        Assert.Empty(writeMembers);
    }

    private static DbContextStream<TestState> CreateTypedStream()
    {
        return new DbContextStream<TestState>(new DbStream
        {
            Id = Guid.NewGuid(),
            StreamType = "orders",
            TenantId = Guid.NewGuid()
        });
    }

    private sealed class TestEvent;

    private sealed class TestState : IState
    {
        public void Apply(IEvent @event)
        {
        }
    }
}

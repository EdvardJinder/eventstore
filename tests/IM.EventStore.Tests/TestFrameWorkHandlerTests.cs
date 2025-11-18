using IM.EventStore.Abstractions;
using IM.EventStore.Testing;
using Microsoft.Extensions.Time.Testing;

namespace IM.EventStore.Tests;

public record DecrementCommand(int Amount, DateTimeOffset Time);
public record IncrementedEvent(int Amount);
public record DecrementedEvent(int Amount, DateTimeOffset Time);
public class TestState : IState
{
    public int Value { get; set; } = 0;
    public void Apply(IEvent @event)
    {
        switch (@event)
        {
            case Event<IncrementedEvent> e:
                Value += e.Data.Amount;
                break;
            case Event<DecrementedEvent> e:
                Value -= e.Data.Amount;
                break;
        }
    }
}
public class TestHandler : IHandler<TestState, DecrementCommand>
{
    public static void Handle(IStream<TestState> stream, DecrementCommand command)
    {
        if (command.Amount <= 0)
        {
            throw new InvalidOperationException("Amount cannot be negative or zero");
        }

        if (stream.State.Value - command.Amount < 0)
        {
            throw new InvalidOperationException("Insufficient funds");
        }

        stream.State.Value -= command.Amount;

        stream.Append(new DecrementedEvent(command.Amount, command.Time));
    }
}


public class TestFrameWorkHandlerTests : HandlerTest<TestHandler, TestState, DecrementCommand>
{

    [Fact]
    public void should_handle_command_and_produce_events()
    {
        Given(new IncrementedEvent(10));
        When(new DecrementCommand(5, TimeProvider.GetUtcNow()));
        Then(new DecrementedEvent(5, TimeProvider.GetUtcNow()));
    }

    [Fact]
    public void should_handle_command_and_produce_events2()
    {
        Given(new IncrementedEvent(10));
        When(new DecrementCommand(1, TimeProvider.GetUtcNow()));
        Then(new DecrementedEvent(1, TimeProvider.GetUtcNow()));
    }

    [Fact]
    public void should_throw_exception_on_insufficient_funds()
    {
        Given(new IncrementedEvent(4));
        ThrowsWhen<InvalidOperationException>(new DecrementCommand(5, TimeProvider.GetUtcNow()));
    }

    [Fact]
    public void should_throw_exception_on_negative_amount()
    {
        Given(new IncrementedEvent(10));
        ThrowsWhen<InvalidOperationException>(new DecrementCommand(-3, TimeProvider.GetUtcNow()));
    }



}

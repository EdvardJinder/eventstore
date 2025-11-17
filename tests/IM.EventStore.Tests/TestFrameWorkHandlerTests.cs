using IM.EventStore.Abstractions;
using IM.EventStore.Testing;

namespace IM.EventStore.Tests;

public record DecrementCommand(int Amount);
public record IncrementedEvent(int Amount);
public record DecrementedEvent(int Amount);
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
    static IReadOnlyCollection<object> IHandler<TestState, DecrementCommand>.Handle(TestState state, DecrementCommand command)
    {
        if (command.Amount <= 0)
        {
            throw new InvalidOperationException("Amount cannot be negative or zero");
        }

        if (state.Value - command.Amount < 0)
        {
            throw new InvalidOperationException("Insufficient funds");
        }

        state.Value -= command.Amount;
        return [new DecrementedEvent(command.Amount)];
    }
}

public class TestFrameWorkHandlerTests : HandlerTest<TestHandler, TestState, DecrementCommand>
{
   

    [Fact]
    public void should_handle_command_and_produce_events()
    {
        Given(new IncrementedEvent(10));
        When(new DecrementCommand(5));
        Then(new DecrementedEvent(5));
    }

    [Fact]
    public void should_throw_exception_on_insufficient_funds()
    {
        Given(new IncrementedEvent(4));
        ThrowsWhen<InvalidOperationException>(new DecrementCommand(5));
    }

    [Fact]
    public void should_throw_exception_on_negative_amount()
    {
        Given(new IncrementedEvent(10));
        ThrowsWhen<InvalidOperationException>(new DecrementCommand(-3));
    }



}

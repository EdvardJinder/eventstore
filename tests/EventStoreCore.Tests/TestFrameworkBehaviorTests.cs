using EventStoreCore.Abstractions;
using EventStoreCore.Testing;

namespace EventStoreCore.Tests;

public record AccountOpened(int AccountNumber, int Balance);
public record DepositedEvent(int Amount);
public record DecrementedEvent(int Amount, DateTimeOffset Time);

public class TestState : IState
{
    public int Balance { get; private set; }

    public void Apply(IEvent @event)
    {
        switch (@event)
        {
            case Event<AccountOpened> e:
                Balance = e.Data.Balance;
                break;
            case Event<DepositedEvent> e:
                Balance += e.Data.Amount;
                break;
            case Event<DecrementedEvent> e:
                Balance -= e.Data.Amount;
                break;
        }
    }
}

public static class TestCommands
{
    public static void OpenAccount(this IStream<TestState> stream, int accountNumber, int balance)
    {
        if (accountNumber <= 0)
        {
            throw new InvalidOperationException("Account number must be positive");
        }

        stream.Append(new AccountOpened(accountNumber, balance));
    }

    public static void Deposit(this IStream<TestState> stream, int amount)
    {
        if (amount <= 0)
        {
            throw new InvalidOperationException("Amount cannot be negative or zero");
        }

        stream.Append(new DepositedEvent(amount));
    }

    public static void Decrement(this IStream<TestState> stream, int amount, DateTimeOffset time)
    {
        if (amount <= 0)
        {
            throw new InvalidOperationException("Amount cannot be negative or zero");
        }

        if (stream.State.Balance - amount < 0)
        {
            throw new InvalidOperationException("Insufficient funds");
        }

        stream.Append(new DecrementedEvent(amount, time));
    }
}

public class TestFrameworkBehaviorTests : StreamBehaviorTest<TestState>
{
    [Fact]
    public void should_append_events_in_when()
    {
        var now = DateTimeOffset.UtcNow;

        When(s =>
        {
            s.OpenAccount(123, 10);
            s.Decrement(4, now);
        });

        Then(
            new AccountOpened(123, 10),
            new DecrementedEvent(4, now));
    }

    [Fact]
    public void given_events_are_not_asserted_in_then()
    {
        var now = DateTimeOffset.UtcNow;

        Given(new AccountOpened(123, 10));

        When(s => s.Decrement(3, now));

        Then(new DecrementedEvent(3, now));
    }

    [Fact]
    public void should_throw_on_invalid_command()
    {
        Assert.Throws<InvalidOperationException>(() =>
        {
            When(s => s.Decrement(1, DateTimeOffset.UtcNow));
        });
    }
}

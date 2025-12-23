using EventStoreCore.Abstractions;
using EventStoreCore.Testing;
using Shouldly;

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
    public void given_multiple_events_apply_to_state_in_when()
    {
        var now = DateTimeOffset.UtcNow;

        Given(
            new AccountOpened(123, 10),
            new DepositedEvent(5));

        Throws<InvalidOperationException>(s => s.Decrement(20, now));
    }

    [Fact]
    public void given_events_are_not_asserted_in_then()
    {
        var now = DateTimeOffset.UtcNow;

        Given(
            new AccountOpened(123, 10),
            new DepositedEvent(2));

        When(s => s.Decrement(3, now));

        Then(new DecrementedEvent(3, now));
    }

    [Fact]
    public void should_throw_on_invalid_command()
    {
        Throws<InvalidOperationException>(s => s.Decrement(1, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void then_with_no_new_events_should_pass()
    {
        Given(new AccountOpened(123, 10));

        Then();
    }

    [Fact]
    public void multiple_when_calls_are_all_asserted_in_then()
    {
        var now = DateTimeOffset.UtcNow;

        Given(new AccountOpened(123, 10));

        When(s => s.Deposit(5));
        When(s => s.Decrement(3, now));

        Then(
            new DepositedEvent(5),
            new DecrementedEvent(3, now));
    }

    [Fact]
    public void throws_without_append_keeps_then_empty()
    {
        Given(new AccountOpened(123, 10));

        Throws<InvalidOperationException>(_ => throw new InvalidOperationException("Boom"));

        Then();
    }

    [Fact]
    public void throws_returns_the_exception()
    {
        var ex = Throws<InvalidOperationException>(_ => throw new InvalidOperationException("Boom"));

        ex.Message.ShouldBe("Boom");
    }

    [Fact]
    public void when_does_nothing_then_should_pass_with_no_events()
    {
        When(_ => { });

        Then();
    }

    [Fact]
    public void then_should_fail_on_unexpected_events()
    {
        When(s => s.Deposit(1));

        var ex = Assert.Throws<Shouldly.ShouldAssertException>(() => Then());

        ex.Message.ShouldContain("Expected count: 0, Actual count: 1");
        ex.Message.ShouldContain("DepositedEvent");
    }

    [Fact]
    public void then_should_fail_on_event_order_mismatch()
    {
        var now = DateTimeOffset.UtcNow;

        When(s =>
        {
            s.Deposit(5);
            s.Decrement(3, now);
        });

        var ex = Assert.Throws<Shouldly.ShouldAssertException>(() =>
            Then(
                new DecrementedEvent(3, now),
                new DepositedEvent(5)));

        ex.Message.ShouldContain("Event mismatch.");
        ex.Message.ShouldContain("Expected count: 2, Actual count: 2");
        ex.Message.ShouldContain(nameof(DepositedEvent));
        ex.Message.ShouldContain(nameof(DecrementedEvent));
    }

    [Fact]
    public void then_should_include_differences_for_mismatched_values()
    {
        var now = DateTimeOffset.UtcNow;

        Given(new AccountOpened(123, 10));

        When(s => s.Decrement(3, now));

        var ex = Assert.Throws<Shouldly.ShouldAssertException>(() =>
            Then(new DecrementedEvent(4, now)));

        ex.Message.ShouldContain("Differences:");
        ex.Message.ShouldContain("Amount");
    }

    [Fact]
    public void throws_after_append_still_records_events()
    {
        var now = DateTimeOffset.UtcNow;

        Throws<InvalidOperationException>(s =>
        {
            s.Deposit(2);
            throw new InvalidOperationException("Boom");
        });

        Then(new DepositedEvent(2));
    }

    [Fact]
    public void then_state_can_assert_current_state()
    {
        Given(
            new AccountOpened(123, 10),
            new DepositedEvent(5));

        When(s => s.Decrement(3, DateTimeOffset.UtcNow));

        ThenState(state => state.Balance.ShouldBe(12));
    }

    [Fact]
    public void then_state_reflects_only_given_when_no_new_events()
    {
        Given(
            new AccountOpened(123, 10),
            new DepositedEvent(2));

        ThenState(state => state.Balance.ShouldBe(12));
    }

    [Fact]
    public void then_state_reflects_events_from_multiple_when_calls()
    {
        Given(new AccountOpened(123, 10));

        When(s => s.Deposit(2));
        When(s => s.Decrement(5, DateTimeOffset.UtcNow));

        ThenState(state => state.Balance.ShouldBe(7));
    }

    [Fact]
    public void then_state_should_fail_with_clear_assertion_message()
    {
        Given(new AccountOpened(123, 10));

        var ex = Assert.Throws<Shouldly.ShouldAssertException>(() =>
            ThenState(state => state.Balance.ShouldBe(999)));

        ex.Message.ShouldContain("999");
        ex.Message.ShouldContain("10");
    }

    [Fact]
    public void then_state_should_surface_exceptions_from_assertion()
    {
        Given(new AccountOpened(123, 10));

        Assert.Throws<InvalidOperationException>(() =>
            ThenState(_ => throw new InvalidOperationException("State check failed")));
    }
}

public class TestFrameworkBehaviorToleranceTests : StreamBehaviorTest<TestState>
{
    protected override int MaxMillisecondsDateDifference => 1000;

    [Fact]
    public void then_can_tolerate_timestamp_differences_when_overridden()
    {
        var now = DateTimeOffset.UtcNow;

        Given(new AccountOpened(123, 10));

        When(s => s.Decrement(1, now));

        Then(new DecrementedEvent(1, now.AddMilliseconds(500)));
    }
}

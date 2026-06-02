using EventStoreCore.Abstractions;
using EventStoreCore.Hangfire;
using Hangfire;
using Hangfire.Common;
using Hangfire.MemoryStorage;
using Hangfire.Server;
using Hangfire.States;
using Microsoft.Extensions.DependencyInjection;

namespace EventStoreCore.Tests;

[Collection(SchedulerTestCollection.Name)]
public class HangfireSchedulerTests
{
    [Fact]
    public async Task should_invoke_hangfire_action_once_for_replayed_event()
    {
        var provider = BuildProvider(s =>
        {
            s.On<OrderPlaced>().Hangfire(static (e, client, _, _) =>
            {
                client.Create(
                    Job.FromExpression(() => HangfireProbe.Run(e.Data.OrderId)),
                    new ScheduledState(TimeSpan.FromMinutes(15)));
                return ValueTask.CompletedTask;
            });
        });
        var subscription = provider.GetServices<ISubscription>().OfType<HangfireSubscription>().Single();
        var placed = new TestEvent<OrderPlaced>(Guid.NewGuid(), new OrderPlaced { OrderId = Guid.NewGuid() });

        await subscription.Handle(placed, TestContext.Current.CancellationToken);
        await subscription.Handle(placed, TestContext.Current.CancellationToken);

        Assert.Equal(1, provider.GetRequiredService<JobStorage>().GetMonitoringApi().ScheduledCount());
    }

    [Fact]
    public async Task should_run_each_registration_for_same_event_and_args()
    {
        var provider = BuildProvider(s =>
        {
            s.On<OrderPlaced>().Hangfire("payment-reminder", static (e, client, _, _) =>
            {
                client.Create(
                    Job.FromExpression(() => HangfireProbe.Remind(e.Data.OrderId)),
                    new ScheduledState(TimeSpan.FromMinutes(15)));
                return ValueTask.CompletedTask;
            });
            s.On<OrderPlaced>().Hangfire("payment-escalation", static (e, client, _, _) =>
            {
                client.Create(
                    Job.FromExpression(() => HangfireProbe.Escalate(e.Data.OrderId)),
                    new ScheduledState(TimeSpan.FromMinutes(15)));
                return ValueTask.CompletedTask;
            });
        });
        var subscription = provider.GetServices<ISubscription>().OfType<HangfireSubscription>().Single();
        var placed = new TestEvent<OrderPlaced>(Guid.NewGuid(), new OrderPlaced { OrderId = Guid.NewGuid() });

        await subscription.Handle(placed, TestContext.Current.CancellationToken);
        await subscription.Handle(placed, TestContext.Current.CancellationToken);

        Assert.Equal(2, provider.GetRequiredService<JobStorage>().GetMonitoringApi().ScheduledCount());
    }

    [Fact]
    public async Task should_retry_same_event_when_provider_action_fails_before_completion()
    {
        var attempts = 0;
        var provider = BuildProvider(s =>
        {
            s.On<OrderPlaced>().Hangfire((e, client, _, _) =>
            {
                if (Interlocked.Increment(ref attempts) == 1)
                {
                    throw new InvalidOperationException("transient scheduler failure");
                }

                client.Create(
                    Job.FromExpression(() => HangfireProbe.Run(e.Data.OrderId)),
                    new ScheduledState(TimeSpan.FromMinutes(15)));
                return ValueTask.CompletedTask;
            });
        });
        var subscription = provider.GetServices<ISubscription>().OfType<HangfireSubscription>().Single();
        var placed = new TestEvent<OrderPlaced>(Guid.NewGuid(), new OrderPlaced { OrderId = Guid.NewGuid() });

        await Assert.ThrowsAsync<InvalidOperationException>(() => subscription.Handle(placed, TestContext.Current.CancellationToken));
        await subscription.Handle(placed, TestContext.Current.CancellationToken);
        await subscription.Handle(placed, TestContext.Current.CancellationToken);

        Assert.Equal(2, attempts);
        Assert.Equal(1, provider.GetRequiredService<JobStorage>().GetMonitoringApi().ScheduledCount());
    }

    [Fact]
    public async Task should_pass_scoped_service_provider_to_action()
    {
        var provider = BuildProvider(s =>
        {
            s.On<OrderPlaced>().Hangfire(static (_, _, sp, _) =>
            {
                sp.GetRequiredService<ScopedCallbackProbe>().Use();
                return ValueTask.CompletedTask;
            });
        },
        services =>
        {
            services.AddSingleton<ScopedCallbackProbeLog>();
            services.AddScoped<ScopedCallbackProbe>();
        },
        new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
        var subscription = provider.GetServices<ISubscription>().OfType<HangfireSubscription>().Single();
        var placed = new TestEvent<OrderPlaced>(Guid.NewGuid(), new OrderPlaced { OrderId = Guid.NewGuid() });

        await subscription.Handle(placed, TestContext.Current.CancellationToken);

        var log = provider.GetRequiredService<ScopedCallbackProbeLog>();
        Assert.Equal(1, log.Used);
        Assert.Equal(1, log.Disposed);
    }

    [Fact]
    public async Task should_allow_action_to_use_hangfire_replacement_semantics()
    {
        var createdJobId = (string?)null;
        var provider = BuildProvider(s =>
        {
            s.On<OrderPlaced>().Hangfire(static (e, client, sp, _) =>
            {
                var state = sp.GetRequiredService<ReplacementState>();
                if (state.JobId is not null)
                {
                    client.Delete(state.JobId);
                }

                state.JobId = client.Create(
                    Job.FromExpression(() => HangfireProbe.Run(e.Data.OrderId)),
                    new ScheduledState(TimeSpan.FromMinutes(15)));
                return ValueTask.CompletedTask;
            });
        }, services => services.AddSingleton(new ReplacementState()));
        var subscription = provider.GetServices<ISubscription>().OfType<HangfireSubscription>().Single();
        var first = new TestEvent<OrderPlaced>(Guid.NewGuid(), new OrderPlaced { OrderId = Guid.NewGuid() });
        var second = new TestEvent<OrderPlaced>(Guid.NewGuid(), new OrderPlaced { OrderId = Guid.NewGuid() });

        await subscription.Handle(first, TestContext.Current.CancellationToken);
        createdJobId = provider.GetRequiredService<ReplacementState>().JobId;
        await subscription.Handle(second, TestContext.Current.CancellationToken);

        Assert.NotNull(createdJobId);
        Assert.NotEqual(createdJobId, provider.GetRequiredService<ReplacementState>().JobId);
        Assert.Equal(1, provider.GetRequiredService<JobStorage>().GetMonitoringApi().ScheduledCount());
    }

    [Fact]
    public async Task should_execute_enqueued_hangfire_job()
    {
        HangfireProbe.Reset();
        await using var provider = BuildProvider(s =>
        {
            s.On<OrderPlaced>().Hangfire(static (e, client, _, _) =>
            {
                client.Create(
                    Job.FromExpression(() => HangfireProbe.Run(e.Data.OrderId)),
                    new EnqueuedState());
                return ValueTask.CompletedTask;
            });
        });
        var subscription = provider.GetServices<ISubscription>().OfType<HangfireSubscription>().Single();
        var placed = new TestEvent<OrderPlaced>(Guid.NewGuid(), new OrderPlaced { OrderId = Guid.NewGuid() });

        await subscription.Handle(placed, TestContext.Current.CancellationToken);
        using var server = new BackgroundJobServer(
            new BackgroundJobServerOptions
            {
                WorkerCount = 1,
                Queues = ["default"],
                SchedulePollingInterval = TimeSpan.FromMilliseconds(50)
            },
            provider.GetRequiredService<JobStorage>());

        await SchedulerTestWait.WaitForAsync(() => HangfireProbe.Ran.Contains(placed.Data.OrderId), TimeSpan.FromSeconds(5));
    }

    private static ServiceProvider BuildProvider(
        Action<Microsoft.Extensions.DependencyInjection.IServiceCollection>? configureServices = null)
    {
        return BuildProvider(_ => { }, configureServices);
    }

    private static ServiceProvider BuildProvider(
        Action<EventStoreCore.Scheduling.ISchedulerBuilder> configureScheduler,
        Action<Microsoft.Extensions.DependencyInjection.IServiceCollection>? configureServices = null,
        ServiceProviderOptions? providerOptions = null)
    {
        var services = new ServiceCollection();
        var storage = new MemoryStorage();
        services.AddSingleton<JobStorage>(storage);
        services.AddSingleton<IBackgroundJobClient>(sp => new BackgroundJobClient(sp.GetRequiredService<JobStorage>()));
        services.AddLogging();
        configureServices?.Invoke(services);
        services.AddEventStore(builder => builder.AddScheduler(s =>
        {
            s.UsingHangfire();
            configureScheduler(s);
        }));

        return providerOptions is null
            ? services.BuildServiceProvider()
            : services.BuildServiceProvider(providerOptions);
    }

    private sealed class ReplacementState
    {
        public string? JobId { get; set; }
    }

    private sealed class ScopedCallbackProbe(ScopedCallbackProbeLog log) : IDisposable
    {
        public void Use() => log.Used++;

        public void Dispose() => log.Disposed++;
    }

    private sealed class ScopedCallbackProbeLog
    {
        public int Used { get; set; }

        public int Disposed { get; set; }
    }
}

public static class HangfireProbe
{
    private static readonly object Gate = new();

    public static List<Guid> Ran { get; } = [];

    public static void Run(Guid orderId)
    {
        lock (Gate)
        {
            Ran.Add(orderId);
        }
    }

    public static void Remind(Guid orderId)
    {
    }

    public static void Escalate(Guid orderId)
    {
    }

    public static void Reset()
    {
        lock (Gate)
        {
            Ran.Clear();
        }
    }
}

internal static class SchedulerTestWait
{
    public static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout)
    {
        var started = DateTime.UtcNow;
        while (DateTime.UtcNow - started < timeout)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(25, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException("Condition was not met within the allotted timeout.");
    }
}

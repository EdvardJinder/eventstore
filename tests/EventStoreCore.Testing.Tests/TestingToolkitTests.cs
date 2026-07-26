using System.Reflection;
using System.Text.Json.Nodes;
using EventStoreCore.Abstractions;
using EventStoreCore.Testing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;

namespace EventStoreCore.Testing.Tests;

public sealed class TestingToolkitTests
{
    private sealed record AccountCredited(int Amount);
    private sealed record AccountDebited(int Amount);

    private sealed class BalanceSnapshot
    {
        public int Balance { get; set; }
    }

    private sealed class ProjectionState
    {
        public List<string> Calls { get; } = [];
    }

    [ProjectionVersion(3)]
    private sealed class BalanceProjection : IProjection<BalanceSnapshot>
    {
        public static Task Evolve(
            BalanceSnapshot snapshot,
            IEvent @event,
            IProjectionContext context,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var state = (ProjectionState)context.ProviderState!;
            state.Calls.Add($"evolve:{@event.Sequence}");
            if (@event is IEvent<AccountCredited> credited)
            {
                snapshot.Balance += credited.Data.Amount;
            }

            return Task.CompletedTask;
        }

        public static Task ClearAsync(IProjectionContext context, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            ((ProjectionState)context.ProviderState!).Calls.Add("clear");
            return Task.CompletedTask;
        }
    }

    private sealed record CurrentAccountEvent(int Amount, string Currency);

    private sealed class RecordingSubscription : ISubscription
    {
        public List<IEvent> Handled { get; } = [];

        public Task Handle(IEvent @event, CancellationToken ct)
        {
            Handled.Add(@event);
            return Task.CompletedTask;
        }
    }

    private sealed class FailOnceSubscription : ISubscription
    {
        private bool _failed;

        public List<Guid> AttemptedIds { get; } = [];

        public Task Handle(IEvent @event, CancellationToken ct)
        {
            AttemptedIds.Add(@event.Id);
            if (!_failed)
            {
                _failed = true;
                throw new InvalidOperationException("Transient failure.");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class TypedRecordingSubscription : ISubscription<AccountCredited>
    {
        public List<IEvent<AccountCredited>> Handled { get; } = [];

        public Task Handle(IEvent<AccountCredited> @event, CancellationToken ct)
        {
            Handled.Add(@event);
            return Task.CompletedTask;
        }
    }

    private sealed class ClockDrivenDaemon(TimeProvider timeProvider) : BackgroundService
    {
        public int Cycles { get; private set; }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMinutes(1), timeProvider, stoppingToken);
                Cycles++;
            }
        }
    }

    [Fact]
    public void TestEvent_preserves_explicit_identity_ordering_and_metadata()
    {
        var streamId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var timestamp = DateTimeOffset.Parse("2026-01-02T03:04:05Z");
        var @event = new TestEvent<AccountCredited>(
            new AccountCredited(5),
            id: Guid.NewGuid(),
            streamId: streamId,
            tenantId: tenantId,
            version: 4,
            sequence: 12,
            timestamp: timestamp,
            typeName: "account_credited",
            streamType: "accounts",
            metadata: new EventMetadata(correlationId: correlationId));

        Assert.Equal(streamId, @event.StreamId);
        Assert.Equal(tenantId, @event.TenantId);
        Assert.Equal(4, @event.Version);
        Assert.Equal(12, @event.Sequence);
        Assert.Equal(timestamp, @event.Timestamp);
        Assert.Equal("account_credited", @event.Metadata.EventType);
        Assert.Equal("accounts", @event.Metadata.StreamType);
        Assert.Equal(correlationId, @event.Metadata.CorrelationId);
    }

    [Fact]
    public async Task Projection_harness_clears_before_rebuilding_in_event_order()
    {
        var projectionState = new ProjectionState();
        var harness = new ProjectionTestHarness<BalanceProjection, BalanceSnapshot>(
            providerState: projectionState);
        var snapshot = new BalanceSnapshot { Balance = 99 };
        var events = new IEvent[]
        {
            new TestEvent<AccountCredited>(new AccountCredited(2), sequence: 1),
            new TestEvent<AccountCredited>(new AccountCredited(3), sequence: 2)
        };

        await harness.RebuildAsync(events, _ => snapshot, TestContext.Current.CancellationToken);

        Assert.Equal(104, snapshot.Balance);
        Assert.Equal(["clear", "evolve:1", "evolve:2"], projectionState.Calls);
        Assert.Equal(3, harness.ProjectionVersion);
        Assert.Same(projectionState, harness.Context.ProviderState);
    }

    [Fact]
    public void Schema_upcaster_harness_runs_the_real_version_chain()
    {
        var steps = new List<int>();
        var harness = new SchemaUpcasterTestHarness<CurrentAccountEvent>(
            "account_credited",
            currentSchemaVersion: 3,
            builder => builder
                .AddUpcaster(1, 2, json =>
                {
                    steps.Add(1);
                    var node = JsonNode.Parse(json)!.AsObject();
                    node["Amount"] = node["LegacyAmount"]!.GetValue<int>();
                    node.Remove("LegacyAmount");
                    return node.ToJsonString();
                })
                .AddUpcaster(2, 3, json =>
                {
                    steps.Add(2);
                    var node = JsonNode.Parse(json)!.AsObject();
                    node["Currency"] = "SEK";
                    return node.ToJsonString();
                }));

        var result = harness.Upcast("""{"LegacyAmount":42}""", storedSchemaVersion: 1);

        Assert.Equal([1, 2], steps);
        Assert.Equal(new CurrentAccountEvent(42, "SEK"), result);
    }

    [Fact]
    public async Task Optimistic_concurrency_harness_returns_the_domain_conflict()
    {
        using var dbContext = new TestDbContext(Guid.NewGuid().ToString("N"));
        dbContext.Database.EnsureCreated();
        var streamId = Guid.NewGuid();
        var harness = new OptimisticConcurrencyTestHarness(
            dbContext.Streams,
            "accounts",
            streamId);

        await harness.AppendAsync(
            ExpectedVersion.NoStream,
            [new AccountCredited(1)],
            TestContext.Current.CancellationToken);

        var exception = await harness.ExpectConflictAsync(
            ExpectedVersion.NoStream,
            [new AccountCredited(2)],
            TestContext.Current.CancellationToken);

        Assert.Equal(streamId, exception.StreamId);
        Assert.Equal("accounts", exception.StreamType);
        Assert.Equal(ExpectedVersion.NoStream, exception.ExpectedVersion);
        Assert.Equal(1, exception.ActualVersion);
    }

    [Fact]
    public async Task Subscription_harness_applies_all_filter_categories_and_advances_filtered_events()
    {
        var tenantId = Guid.NewGuid();
        var streamId = Guid.NewGuid();
        var subscription = new RecordingSubscription();
        await using var harness = SubscriptionTestHarness.For(
            subscription,
            options =>
            {
                options.Name = "account-credit-audit";
                options.IncludeLogicalEventType("account_credited");
                options.IncludeEventType<AccountCredited>();
                options.IncludeStreamType("accounts");
                options.IncludeStream(streamId);
                options.IncludeTenant(tenantId);
            });

        harness.Given(
            new TestEvent<AccountCredited>(
                new AccountCredited(1),
                id: Guid.NewGuid(),
                streamId: streamId,
                tenantId: tenantId,
                version: 1,
                sequence: 1,
                typeName: "legacy_credit",
                streamType: "accounts"),
            new TestEvent<AccountDebited>(
                new AccountDebited(1),
                id: Guid.NewGuid(),
                streamId: streamId,
                tenantId: tenantId,
                version: 2,
                sequence: 2,
                typeName: "account_credited",
                streamType: "accounts"),
            new TestEvent<AccountCredited>(
                new AccountCredited(2),
                id: Guid.NewGuid(),
                streamId: streamId,
                tenantId: tenantId,
                version: 1,
                sequence: 3,
                typeName: "account_credited",
                streamType: "other"),
            new TestEvent<AccountCredited>(
                new AccountCredited(3),
                id: Guid.NewGuid(),
                streamId: Guid.NewGuid(),
                tenantId: tenantId,
                version: 1,
                sequence: 4,
                typeName: "account_credited",
                streamType: "accounts"),
            new TestEvent<AccountCredited>(
                new AccountCredited(4),
                id: Guid.NewGuid(),
                streamId: streamId,
                tenantId: Guid.NewGuid(),
                version: 1,
                sequence: 5,
                typeName: "account_credited",
                streamType: "accounts"),
            new TestEvent<AccountCredited>(
                new AccountCredited(5),
                id: Guid.NewGuid(),
                streamId: streamId,
                tenantId: tenantId,
                version: 3,
                sequence: 6,
                typeName: "account_credited",
                streamType: "accounts"));

        var processed = await harness.ProcessUntilIdleAsync(
            cancellationToken: TestContext.Current.CancellationToken);
        var status = await harness.GetStatusAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(6, processed);
        Assert.Single(subscription.Handled);
        Assert.Equal(6, subscription.Handled[0].Sequence);
        Assert.Equal(6, status.Position);
        Assert.Equal(SubscriptionState.Active, status.State);
    }

    [Fact]
    public async Task Subscription_harness_retries_the_same_event_and_can_replay_it()
    {
        var eventId = Guid.NewGuid();
        var subscription = new FailOnceSubscription();
        await using var harness = SubscriptionTestHarness.For(
            subscription,
            options => options.Name = "retry-credit",
            daemon => daemon.MaxRetryAttempts = 2);
        harness.Given(new TestEvent<AccountCredited>(
            new AccountCredited(5),
            id: eventId,
            sequence: 1,
            typeName: "account_credited"));

        Assert.Equal(
            0,
            await harness.ProcessNextBatchAsync(
                cancellationToken: TestContext.Current.CancellationToken));
        var faulted = await harness.GetStatusAsync(
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(SubscriptionState.Faulted, faulted.State);
        Assert.Equal(1, faulted.AttemptCount);

        await harness.RetryFailedEventAsync(
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(
            1,
            await harness.ProcessNextBatchAsync(
                cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal([eventId, eventId], subscription.AttemptedIds);

        await harness.ReplayAsync(
            startSequence: 1,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(
            1,
            await harness.ProcessNextBatchAsync(
                cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal([eventId, eventId, eventId], subscription.AttemptedIds);
    }

    [Fact]
    public async Task Subscription_harness_supports_typed_handlers_and_unknown_event_policies()
    {
        var typed = new TypedRecordingSubscription();
        UnknownEventContext? unknown = null;
        await using var harness = SubscriptionTestHarness.For<TypedRecordingSubscription, AccountCredited>(
            typed,
            options =>
            {
                options.Name = "typed-credit";
                options.HandleUnknown((context, _) =>
                {
                    unknown = context;
                    return ValueTask.CompletedTask;
                });
            });
        harness.GivenUnknown(
            "legacy_credit",
            "Missing.Contracts.LegacyCredit, Missing.Contracts",
            """{"Amount":9}""",
            eventId: Guid.NewGuid(),
            sequence: 1);
        harness.Given(
            new TestEvent<AccountCredited>(
                new AccountCredited(8),
                id: Guid.NewGuid(),
                sequence: 2,
                typeName: "account_credited"));

        Assert.Equal(
            2,
            await harness.ProcessUntilIdleAsync(
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Single(typed.Handled);
        Assert.Equal(8, typed.Handled[0].Data.Amount);
        Assert.NotNull(unknown);
        Assert.Equal("legacy_credit", unknown.LogicalTypeName);
        Assert.Equal("""{"Amount":9}""", unknown.Data);
        Assert.Equal(
            2,
            (await harness.GetStatusAsync(
                cancellationToken: TestContext.Current.CancellationToken)).Position);
    }

    [Fact]
    public async Task Subscription_harness_quarantines_unknown_events_without_exposing_rows()
    {
        await using var harness = SubscriptionTestHarness.For(
            new RecordingSubscription(),
            options =>
            {
                options.Name = "quarantine-credit";
                options.UnknownEventPolicy = UnknownEventPolicy.Quarantine;
            });
        harness.GivenUnknown(
            "removed_event",
            "Missing.RemovedEvent, Missing",
            "{}",
            sequence: 1);

        Assert.Equal(
            0,
            await harness.ProcessNextBatchAsync(
                cancellationToken: TestContext.Current.CancellationToken));
        var status = await harness.GetStatusAsync(
            cancellationToken: TestContext.Current.CancellationToken);
        var failed = await harness.GetFailedEventAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(SubscriptionState.DeadLettered, status.State);
        Assert.NotNull(failed);
        Assert.Equal("removed_event", failed.EventType);
    }

    [Theory]
    [InlineData(UnknownEventPolicy.Skip, SubscriptionState.Active, 1, false)]
    [InlineData(UnknownEventPolicy.Fail, SubscriptionState.Faulted, 0, true)]
    public async Task Subscription_harness_models_skip_and_fail_unknown_event_policies(
        UnknownEventPolicy policy,
        SubscriptionState expectedState,
        int expectedProcessed,
        bool expectsFailure)
    {
        await using var harness = SubscriptionTestHarness.For(
            new RecordingSubscription(),
            options =>
            {
                options.Name = $"unknown-{policy}";
                options.UnknownEventPolicy = policy;
            });
        harness.GivenUnknown(
            "unknown_event",
            "Missing.UnknownEvent, Missing",
            "{}",
            sequence: 1);

        Assert.Equal(
            expectedProcessed,
            await harness.ProcessNextBatchAsync(
                cancellationToken: TestContext.Current.CancellationToken));
        var status = await harness.GetStatusAsync(
            cancellationToken: TestContext.Current.CancellationToken);
        var failed = await harness.GetFailedEventAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(expectedState, status.State);
        Assert.Equal(expectsFailure, failed is not null);
    }

    [Fact]
    public async Task Daemon_harness_advances_a_shared_fake_clock_until_observable_work_completes()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var daemon = new ClockDrivenDaemon(clock);
        await using var harness = new DaemonTestHarness(daemon, clock);

        await harness.StartAsync(TestContext.Current.CancellationToken);
        await harness.RunUntilAsync(
            _ => Task.FromResult(daemon.Cycles == 2),
            TimeSpan.FromMinutes(1),
            maxAttempts: 5,
            TestContext.Current.CancellationToken);

        Assert.Equal(2, daemon.Cycles);
        Assert.True(harness.IsRunning);
        await harness.StopAsync(TestContext.Current.CancellationToken);
        Assert.False(harness.IsRunning);
    }

    [Fact]
    public void Testing_public_api_does_not_expose_internal_ef_rows()
    {
        var testingAssembly = typeof(SubscriptionTestHarness).Assembly;
        var forbiddenNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "DbEvent",
            "DbStream",
            "DbSubscription",
            "DbProjectionStatus",
            "ProjectionRegistration",
            "SubscriptionRegistration"
        };

        var exposedTypes = testingAssembly.GetExportedTypes()
            .SelectMany(type => type
                .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .SelectMany(GetReferencedTypes))
            .Where(type => type.Assembly == typeof(EventStoreExtensions).Assembly)
            .Select(type => type.Name)
            .Where(forbiddenNames.Contains)
            .Distinct()
            .ToArray();

        Assert.Empty(exposedTypes);
    }

    private static IEnumerable<Type> GetReferencedTypes(MemberInfo member)
    {
        return member switch
        {
            MethodInfo method => method.GetParameters()
                .Select(parameter => parameter.ParameterType)
                .Append(method.ReturnType)
                .SelectMany(Unwrap),
            ConstructorInfo constructor => constructor.GetParameters()
                .Select(parameter => parameter.ParameterType)
                .SelectMany(Unwrap),
            PropertyInfo property => Unwrap(property.PropertyType),
            FieldInfo field => Unwrap(field.FieldType),
            EventInfo @event => Unwrap(@event.EventHandlerType ?? typeof(void)),
            Type nestedType => Unwrap(nestedType),
            _ => []
        };
    }

    private static IEnumerable<Type> Unwrap(Type type)
    {
        if (type.IsArray || type.IsByRef || type.IsPointer)
        {
            return Unwrap(type.GetElementType()!);
        }

        if (type.IsGenericType)
        {
            return new[] { type.GetGenericTypeDefinition() }
                .Concat(type.GetGenericArguments().SelectMany(Unwrap));
        }

        return [type];
    }
}

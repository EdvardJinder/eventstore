namespace EventStoreCore.PackageSmoke;

internal static class PackageConsumer
{
    internal static Type[] IndependentlyShippedContracts =>
    [
        typeof(EventStoreCore.IEventStoreBuilder),
        typeof(EventStoreCore.Abstractions.IEventStore),
        typeof(EventStoreCore.CloudEvents.ICloudEventSubscription),
        typeof(EventStoreCore.Endpoints.RouteBuilderExtensions),
        typeof(EventStoreCore.EventGrid.EventGridSubscriptionExtensions),
        typeof(EventStoreCore.Hangfire.HangfireSchedulerExtensions),
        typeof(EventStoreCore.MassTransit.MassTransitEventStoreSubscriptionExtensions),
        typeof(EventStoreCore.Postgres.ModelBuilderExtensions),
        typeof(EventStoreCore.Quartz.QuartzSchedulerExtensions),
        typeof(EventStoreCore.Scheduling.ISchedulerBuilder),
        typeof(EventStoreCore.SDK.IEventStoreEndpointsClient),
        typeof(EventStoreCore.SqlServer.ModelBuilderExtensions),
        typeof(EventStoreCore.Testing.DaemonTestHarness),
        typeof(EventStoreCore.Testing.OptimisticConcurrencyTestHarness),
        typeof(EventStoreCore.Testing.ProjectionTestHarness<,>),
        typeof(EventStoreCore.Testing.SchemaUpcasterTestHarness<>),
        typeof(EventStoreCore.Testing.StreamBehaviorTest<>),
        typeof(EventStoreCore.Testing.SubscriptionTestHarness),
        typeof(EventStoreCore.Testing.SubscriptionTestHarness<>),
        typeof(EventStoreCore.Testing.TestEvent<>),
        typeof(EventStoreCore.TickerQ.TickerQSchedulerExtensions)
    ];
}

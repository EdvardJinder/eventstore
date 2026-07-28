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
        typeof(EventStoreCore.RelationalModelBuilderExtensions),
        typeof(EventStoreCore.RelationalProviderModelOptions),
        typeof(EventStoreCore.Scheduling.ISchedulerBuilder),
        typeof(EventStoreCore.SDK.IEventStoreEndpointsClient),
        typeof(EventStoreCore.Sqlite.ModelBuilderExtensions),
        typeof(EventStoreCore.SqlServer.ModelBuilderExtensions),
        typeof(EventStoreCore.Testing.StreamBehaviorTest<>),
        typeof(EventStoreCore.TickerQ.TickerQSchedulerExtensions)
    ];

    internal static void ConfigureSqlite(Microsoft.EntityFrameworkCore.ModelBuilder modelBuilder)
    {
        EventStoreCore.Sqlite.ModelBuilderExtensions.UseEventStore(modelBuilder);
        EventStoreCore.Sqlite.ModelBuilderExtensions.UseEntityOutbox(modelBuilder);
    }

    internal static void ConfigureCommunityProvider(
        Microsoft.EntityFrameworkCore.ModelBuilder modelBuilder)
    {
        EventStoreCore.RelationalModelBuilderExtensions
            .ConfigureEventStoreRelationalModel(
                modelBuilder,
                new EventStoreCore.RelationalProviderModelOptions("TEXT"));
        EventStoreCore.RelationalModelBuilderExtensions
            .ConfigureEntityOutboxRelationalModel(
                modelBuilder,
                new EventStoreCore.RelationalProviderModelOptions("TEXT"));
    }
}

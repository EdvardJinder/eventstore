using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EventStoreCore;

internal static class SequenceCommitOrder
{
    internal const string AcquireLockSqlAnnotation =
        "EventStoreCore:SequenceCommitOrder:AcquireLockSql";

    internal const string EventsInsertMarkerAnnotation =
        "EventStoreCore:SequenceCommitOrder:EventsInsertMarker";

    internal const string OutboxInsertMarkerAnnotation =
        "EventStoreCore:SequenceCommitOrder:OutboxInsertMarker";

    internal static void AddServices(IServiceCollection services)
    {
        services.TryAddSingleton<SequenceCommitOrderInterceptor>();
    }

    internal static void Configure(
        IServiceProvider serviceProvider,
        DbContextOptionsBuilder optionsBuilder)
    {
        var interceptors = optionsBuilder.Options
            .FindExtension<CoreOptionsExtension>()
            ?.Interceptors;
        var alreadyConfigured = interceptors?.Any(
            interceptor => interceptor is SequenceCommitOrderInterceptor) == true;
        if (!alreadyConfigured)
        {
            optionsBuilder.AddInterceptors(
                serviceProvider.GetRequiredService<SequenceCommitOrderInterceptor>());
        }
    }
}

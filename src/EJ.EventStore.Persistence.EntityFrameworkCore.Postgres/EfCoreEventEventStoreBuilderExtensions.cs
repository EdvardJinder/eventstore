using EJ.EventStore.Persistence.EntityFrameworkCore.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EJ.EventStore.Persistence.EntityFrameworkCore.Postgres;

public static class EfCoreEventEventStoreBuilderExtensions
{
    /// <summary>
    /// Wire EventStore to an already configured DbContext. Only projections/subscriptions are added; provider configuration is assumed to be done elsewhere.
    /// </summary>
    public static IEfCoreEventStoreBuilder<TDbContext> ExistingDbContext<TDbContext>(this IEventStoreBuilder builder)
        where TDbContext : DbContext
    {
        var efBuilder = new EfCoreEventEventStoreBuilder<TDbContext>(builder.Services);

        // Attach projections/interceptors without altering provider configuration.
        efBuilder.Services.AddDbContext<TDbContext>((sp, options) =>
        {
            efBuilder.ConfigureProjections(sp, options);
        });

        builder.UseProvider(efBuilder);

        return efBuilder;
    }

}

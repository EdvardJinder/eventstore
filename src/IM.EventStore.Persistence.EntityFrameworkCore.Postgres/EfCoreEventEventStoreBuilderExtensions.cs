using IM.EventStore.Persistence.EntityFrameworkCore.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IM.EventStore.Persistence.EntityFrameworkCore.Postgres;

public static class EfCoreEventEventStoreBuilderExtensions
{
    public static IEfCoreEventStoreBuilder<TDbContext> UsingPostgres<TDbContext>(this IEventStoreBuilder builder,
           Action<IServiceProvider, DbContextOptionsBuilder> optionsAction,
          Action<IEfCoreEventStoreBuilder<TDbContext>>? configure = null)
        where TDbContext : DbContext
    {

        var efBuilder = new EfCoreEventEventStoreBuilder<TDbContext>(builder.Services);

        configure?.Invoke(efBuilder);

        efBuilder.Services.AddDbContext<TDbContext>((sp, options) =>
        {
            optionsAction(sp, options);
            efBuilder.ConfigureProjections(options);
        });

        return efBuilder;
    }
}

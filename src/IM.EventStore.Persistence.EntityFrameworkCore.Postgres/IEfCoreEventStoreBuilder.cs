using Microsoft.Extensions.DependencyInjection;

namespace IM.EventStore.Persistence.EntityFrameworkCore.Postgres;

public interface IEfCoreEventStoreBuilder<TDbContext>
    where TDbContext : Microsoft.EntityFrameworkCore.DbContext
{
    IServiceCollection Services { get; }
}

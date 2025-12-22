using Microsoft.Extensions.DependencyInjection;

namespace EJ.EventStore.Persistence.EntityFrameworkCore.Postgres;

public interface IEfCoreEventStoreBuilder<TDbContext>
    where TDbContext : Microsoft.EntityFrameworkCore.DbContext
{
    IServiceCollection Services { get; }
}

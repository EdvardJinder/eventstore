using Microsoft.Extensions.DependencyInjection;

namespace EventStoreCore.Persistence.EntityFrameworkCore.Postgres;

public interface IEfCoreEventStoreBuilder<TDbContext>
    where TDbContext : Microsoft.EntityFrameworkCore.DbContext
{
    IServiceCollection Services { get; }
}

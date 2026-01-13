using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EventStoreCore;

public interface IEfCoreEventStoreBuilder<TDbContext>
    where TDbContext : DbContext

{
    IServiceCollection Services { get; }
}

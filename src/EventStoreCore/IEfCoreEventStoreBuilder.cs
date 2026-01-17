using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EventStoreCore;

/// <summary>
/// Builder for configuring EF Core-backed event store services.
/// </summary>
/// <typeparam name="TDbContext">The DbContext type.</typeparam>
public interface IEfCoreEventStoreBuilder<TDbContext>
    where TDbContext : DbContext

{
    /// <summary>
    /// The underlying service collection.
    /// </summary>
    IServiceCollection Services { get; }
}


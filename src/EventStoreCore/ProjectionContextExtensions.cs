using EventStoreCore.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace EventStoreCore;


/// <summary>
/// Extension methods for <see cref="IProjectionContext"/> when using Entity Framework Core.
/// </summary>
public static class ProjectionContextExtensions
{
    extension(IProjectionContext context)
    {
        /// <summary>
        /// Gets the <see cref="DbContext"/> from the projection context.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when the context was not created with an EF Core provider.</exception>
        public DbContext DbContext => context.GetDbContext();
    }

    private static DbContext GetDbContext(this IProjectionContext context)
    {
        if (context.ProviderState is DbContext dbContext)
        {
            return dbContext;
        }

        throw new InvalidOperationException(
            "The projection context was not created with an Entity Framework Core provider. " +
            "Ensure you are using the EF Core extensions in EventStoreCore.");

    }
}

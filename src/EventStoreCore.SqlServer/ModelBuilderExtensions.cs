using EventStoreCore;
using Microsoft.EntityFrameworkCore;

namespace EventStoreCore.SqlServer;

public static class ModelBuilderExtensions
{
    public static void UseEventStore(this ModelBuilder modelBuilder)
    {
        global::EventStoreCore.ModelBuilderExtensions.ConfigureEventStoreModel(modelBuilder);

        modelBuilder.Entity<DbEvent>(entity =>
        {
            entity.Property(e => e.Data)
                .HasColumnType("nvarchar(max)");
        });
    }
}

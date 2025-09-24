using Microsoft.EntityFrameworkCore;
using IM.EventStore;

public class AppDbContext : DbContext
{

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.UseEventStore();

        modelBuilder.Entity<OrderView>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.CustomerId).IsRequired();
            b.Property(x => x.Status).IsRequired();
        });
    }

}

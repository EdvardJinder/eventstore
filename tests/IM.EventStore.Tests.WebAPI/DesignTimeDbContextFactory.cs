using Microsoft.EntityFrameworkCore;
using IM.EventStore;
using Microsoft.EntityFrameworkCore.Design;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Port=5432;Database=im_eventstore_tests;Username=postgres;Password=postgres");
        return new AppDbContext(optionsBuilder.Options);
    }
}
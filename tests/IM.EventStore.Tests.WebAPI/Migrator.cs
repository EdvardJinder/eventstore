using Microsoft.EntityFrameworkCore;
using IM.EventStore;

public class Migrator : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    public Migrator(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await dbContext.Database.MigrateAsync(stoppingToken);
    }
}

using Microsoft.EntityFrameworkCore;
using IM.EventStore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(opts =>
{
    opts.UseNpgsql(builder.Configuration.GetConnectionString("database"));
});

builder.Services.AddHostedService<Migrator>();

builder.Services.AddScoped<PlaceOrder>();

builder.Services.AddProjection<OrderView, OrderViewProjection, AppDbContext>(c =>
{
    c.Handles<OrderPlaced>();
    c.SubscribeFromPresent();
});

var app = builder.Build();

app.UseHttpsRedirection();

app.MapPost("/place-order", async (PlaceOrder placeOrder, PlaceOrder.Command cmd, CancellationToken ct) =>
{
    await placeOrder.HandleAsync(cmd, ct);
    return Results.Ok();
});

app.Run();

public class PlaceOrder(
        AppDbContext dbContext
    )
{
    public record Command(Guid CustomerId);
    public async Task HandleAsync(Command cmd, CancellationToken ct)
    {
        Guid orderId = Guid.NewGuid();

        dbContext.Events.StartStream(orderId, events: [new OrderPlaced(cmd.CustomerId)]);

        await dbContext.SaveChangesAsync(ct);
    }
}

public class OrderView
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public string Status { get; set; } = "Placed";
}

public class OrderViewProjection : IProjection<OrderView>
{
    public Task EvolveAsync(OrderView snapshot, IEvent @event, CancellationToken cancellationToken)
    {
        switch (@event)
        {
            case OrderPlaced op:
                snapshot.Id = @event.StreamId;
                snapshot.CustomerId = op.CustomerId;
                snapshot.Status = "Placed";
                break;
        }
        return Task.CompletedTask;
    }
}
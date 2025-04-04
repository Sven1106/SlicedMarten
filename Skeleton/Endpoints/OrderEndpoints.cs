using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Storage;

namespace Skeleton.Endpoints;

public record PlaceOrderRequest(List<OrderItem> Items);

public record OrderItem(Guid ItemId, uint Quantity);

public abstract class OrderEndpoints : IEndpoint
{
    public static void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/orders", async (PlaceOrderRequest request, IDocumentSession session) =>
            {
                if (request.Items.Count == 0)
                    return Results.BadRequest("Order must contain at least one item.");

                var insufficient = new List<Guid>();

                foreach (var item in request.Items)
                {
                    var inventory = await session.Events.FetchForWriting<InventoryItem>(item.ItemId);
                    if (inventory.Aggregate is null || inventory.Aggregate.Quantity < item.Quantity)
                        insufficient.Add(item.ItemId);
                }

                if (insufficient.Count != 0)
                    return Results.BadRequest($"Not enough stock for: {string.Join(", ", insufficient)}");

                var orderId = Guid.NewGuid();
                var orderPlaced = new OrderPlaced(orderId, request.Items, DateTime.UtcNow);
                session.Events.StartStream<Order>(orderId, orderPlaced);

                foreach (var item in request.Items)
                {
                    session.Events.Append(item.ItemId, new InventoryReserved(item.ItemId, item.Quantity));
                }

                await session.SaveChangesAsync();
                return Results.Created($"/orders/{orderId}", new { orderId });
            })
            .WithName("PlaceOrder")
            .WithDescription("Places an order if inventory is sufficient.")
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest);

        // 📦 Hent et OrderOverview (MultiStreamProjection)
        endpoints.MapGet("/orders/{orderId:guid}/overview", async (
                Guid orderId,
                IQuerySession session) =>
            {
                var overview = await session.LoadAsync<OrderOverview>(orderId);
                return overview is null ? Results.NotFound() : Results.Ok(overview);
            })
            .WithName("GetOrderOverview")
            .WithDescription("Gets an order with inventory status using a multi-stream projection.")
            .Produces<OrderOverview>()
            .Produces(StatusCodes.Status404NotFound);
    }
}

// 📦 Events
public record OrderPlaced(Guid OrderId, List<OrderItem> Items, DateTime PlacedAt);

public record OrderConfirmed(Guid OrderId);

// 🧱 Aggregate
public record Order(Guid Id, List<OrderItem> Items, bool IsConfirmed)
{
    public static Order Create(OrderPlaced e) => new(e.OrderId, e.Items, false);
    public static Order Apply(Order current, OrderConfirmed e) => current with { IsConfirmed = true };
}

// 🔭 MultiStreamProjection viewmodel

public record OrderOverview(Guid Id, DateTime PlacedAt, List<OrderOverview.OrderItemStatus> Items)
{
    public record OrderItemStatus(Guid ItemId, string Name, uint Quantity, uint InventoryLeft);
}

public class OrderOverviewProjection : MultiStreamProjection<OrderOverview, Guid>
{
    public OrderOverviewProjection()
    {
    }

    public static OrderOverview Create(OrderPlaced e) => new(e.OrderId, e.PlacedAt, []);

    public static OrderOverview Apply(OrderOverview current, ItemAddedToInventory e)
    {
        return current;
    }
}

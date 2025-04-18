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
                    if (inventory.Aggregate == null)
                        return Results.BadRequest($"Item with id {item.ItemId} does not exist.");

                    if (inventory.Aggregate.Quantity < item.Quantity)
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

        // 📦 Get an OrderOverview (MultiStreamProjection)
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

public record OrderOverview(Guid Id, DateTime PlacedAt, List<OrderOverview.ItemInfo> Items, int ItemCount, int EventsApplied)
{
    public record ItemInfo(Guid ItemId, string Name, uint Quantity);
}

public class OrderOverviewProjection : MultiStreamProjection<OrderOverview, Guid>
{
    public OrderOverviewProjection() => CustomGrouping(new OrderSlicer());

    public static OrderOverview Create(OrderPlaced e)
    {
        var items = e.Items
            .Select(i => new OrderOverview.ItemInfo(i.ItemId, "", i.Quantity))
            .ToList();

        return new OrderOverview(e.OrderId, e.PlacedAt, items, items.Count, 1);
    }

    public static OrderOverview Apply(OrderOverview view, ItemAddedToInventory e)
    {
        var updatedItems = view.Items
            .Select(i => i.ItemId == e.ItemId ? i with { Name = e.Name } : i)
            .ToList();

        return view with { Items = updatedItems, EventsApplied = view.EventsApplied + 1 };
    }

    public static OrderOverview Apply(OrderOverview view, ItemChangedName e)
    {
        var updatedItems = view.Items
            .Select(i => i.ItemId == e.ItemId ? i with { Name = e.NewName } : i)
            .ToList();

        return view with { Items = updatedItems };
    }

    public class OrderSlicer : IEventSlicer<OrderOverview, Guid>
    {
        public ValueTask<IReadOnlyList<EventSlice<OrderOverview, Guid>>> SliceInlineActions(IQuerySession session, IEnumerable<StreamAction> streams) =>
            throw new NotImplementedException();

        public async ValueTask<IReadOnlyList<TenantSliceGroup<OrderOverview, Guid>>> SliceAsyncEvents(IQuerySession querySession, List<IEvent> events)
        {
            var tenant = Tenant.ForDatabase(querySession.Database);
            var sliceGroup = new TenantSliceGroup<OrderOverview, Guid>(tenant);
            var streamIdToStreamEvents = new Dictionary<Guid, IReadOnlyList<IEvent>>();

            foreach (var @event in events)
            {
                switch (@event)
                {
                    case IEvent<OrderPlaced> orderPlaced:
                    {
                        sliceGroup.AddEvent(orderPlaced.Data.OrderId, orderPlaced);

                        foreach (var orderItem in orderPlaced.Data.Items)
                        {
                            if (streamIdToStreamEvents.TryGetValue(orderItem.ItemId, out var streamEvents) == false)
                            {
                                streamEvents = await querySession.Events.FetchStreamAsync(orderItem.ItemId);
                                streamIdToStreamEvents[orderItem.ItemId] = streamEvents;
                            }

                            foreach (var streamEvent in streamIdToStreamEvents[orderItem.ItemId])
                            {
                                sliceGroup.AddEvent(orderPlaced.Data.OrderId, streamEvent);
                            }
                        }

                        break;
                    }
                    case IEvent<ItemChangedName> itemChanged:
                    {
                        var lookup = await querySession.LoadAsync<ItemToOrders>(itemChanged.Data.ItemId);
                        if (lookup is null) break;
                        foreach (var order in lookup.OrderIds)
                        {
                            sliceGroup.AddEvent(order, itemChanged);
                        }

                        break;
                    }
                }
            }

            return [sliceGroup];
        }
    }
}

public record ItemToOrders(Guid Id, List<Guid> OrderIds);

public class ItemToOrdersProjection : MultiStreamProjection<ItemToOrders, Guid>
{
    public ItemToOrdersProjection() => Identities<OrderPlaced>(e => e.Items.Select(i => i.ItemId).ToList());

    public static ItemToOrders Create(IEvent<OrderPlaced> e) => new(
        Guid.Empty, // Martern overwrites this behind the scenes with slice-id (ItemId), so it really doesnt matter what is written here.
        [e.Data.OrderId]
    );


    public static ItemToOrders Apply(ItemToOrders view, IEvent<OrderPlaced> e)
    {
        if (view.OrderIds.Contains(e.Data.OrderId)) return view;
        var newOrderIds = view.OrderIds.Append(e.Data.OrderId).ToList();

        return view with
        {
            OrderIds = newOrderIds
        };
    }

    // TODO: Figure out when an order should be removed from an item in this lookup table. Will it be too business oriented?
}
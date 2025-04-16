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

public record OrderOverview(Guid Id, DateTime PlacedAt, List<OrderOverview.ItemInfo> Items)
{
    public record ItemInfo(Guid ItemId, string Name, uint Quantity);
}

public class OrderOverviewProjection : MultiStreamProjection<OrderOverview, Guid>
{
    public OrderOverviewProjection()
    {
        CustomGrouping(new OrderSlicer());
    }

    public static OrderOverview Create(OrderPlaced e)
    {
        var items = e.Items
            .Select(i => new OrderOverview.ItemInfo(i.ItemId, "", i.Quantity))
            .ToList();

        return new OrderOverview(e.OrderId, e.PlacedAt, items);
    }

    public static OrderOverview Apply(OrderOverview view, ItemAddedToInventory e)
    {
        var updatedItems = view.Items
            .Select(i => i.ItemId == e.ItemId ? i with { Name = e.Name } : i)
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
            var slices = new TenantSliceGroup<OrderOverview, Guid>(tenant);

            // 1. Slice OrderPlaced direkte
            slices.AddEvents<OrderPlaced>(e => e.OrderId, events);

            // 2. Håndtér ItemAddedToInventory med lookup i dokumentlager
            var itemEvents = events.OfType<IEvent<ItemAddedToInventory>>().ToList();

            foreach (var itemEvent in itemEvents)
            {
                var doc = await querySession.LoadAsync<ItemToOrders>(itemEvent.Data.ItemId);

                if (doc is null) continue;

                foreach (var orderId in doc.Orders)
                {
                    slices.AddEvent(orderId, itemEvent);
                }
            }

            return new List<TenantSliceGroup<OrderOverview, Guid>> { slices };
        }
    }
}

public record ItemToOrders(Guid Id, int OrderCount, int Invocations, List<Guid> Orders);

public class ItemToOrdersProjection : MultiStreamProjection<ItemToOrders, Guid>
{
    public ItemToOrdersProjection()
    {
        Identities<OrderPlaced>(e => e.Items.Select(i => i.ItemId).ToList());
    }

    public static ItemToOrders Create(IEvent<OrderPlaced> e)
    {
        List<Guid> newOrders = [e.Data.OrderId];
        return new ItemToOrders(
            Id: Guid.Empty, // Marten overskriver det korrekt med slice-id (ItemId)
            OrderCount: newOrders.Count,
            Invocations: 1,
            Orders: newOrders
        );
    }

    public static ItemToOrders Apply(ItemToOrders view, IEvent<OrderPlaced> e)
    {
        if (view.Orders.Contains(e.Data.OrderId)) return view;
        var newOrders = view.Orders.Append(e.Data.OrderId).ToList();

        return view with
        {
            Orders = newOrders,
            OrderCount = newOrders.Count,
            Invocations = view.Invocations + 1
        };
    }
}


// public class ItemToOrdersProjection : IProjection
// {
//     public void Apply(IDocumentOperations operations, IReadOnlyList<StreamAction> streams)
//     {
//         throw new NotImplementedException();
//     }
//
//     public async Task ApplyAsync(IDocumentOperations operations, IReadOnlyList<StreamAction> streams, CancellationToken cancellation)
//     {
//         var events = streams
//             .SelectMany(x => x.Events)
//             .OrderBy(s => s.Sequence)
//             .Select(s => s.Data)
//             .ToList();
//
//         var idToDocument = new Dictionary<Guid, ItemToOrders>();
//
//         foreach (var @event in events)
//         {
//             switch (@event)
//             {
//                 case OrderPlaced orderPlaced:
//                 {
//                     // 1. Find alle ItemIds i denne OrderPlaced, som vi ikke allerede har i memory
//                     var itemIdsToLoad = orderPlaced.Items
//                         .Select(x => x.ItemId)
//                         .Where(id => !idToDocument.ContainsKey(id))
//                         .Distinct()
//                         .ToList();
//
//                     // 2. Batch-load dem
//                     if (itemIdsToLoad.Count > 0)
//                     {
//                         var loadedDocuments = await operations.LoadManyAsync<ItemToOrders>(cancellation, itemIdsToLoad);
//                         foreach (var doc in loadedDocuments)
//                         {
//                             idToDocument.TryAdd(doc.Id, doc);
//                         }
//                     }
//
//                     // 3. Opdatér memory state
//                     foreach (var item in orderPlaced.Items)
//                     {
//                         if (!idToDocument.TryGetValue(item.ItemId, out var itemToOrders))
//                         {
//                             itemToOrders = new ItemToOrders(item.ItemId, 0, 0, []);
//                         }
//
//                         if (!itemToOrders.Orders.Contains(orderPlaced.OrderId))
//                         {
//                             var newOrders = new List<Guid>(itemToOrders.Orders) { orderPlaced.OrderId };
//
//                             itemToOrders = itemToOrders with
//                             {
//                                 Orders = newOrders,
//                                 OrderCount = newOrders.Count,
//                                 Invocations = itemToOrders.Invocations + 1
//                             };
//
//                             idToDocument[item.ItemId] = itemToOrders;
//                         }
//                     }
//
//                     break;
//                 }
//             }
//         }
//
//         foreach (var document in idToDocument.Values)
//         {
//             operations.Store(document);
//         }
//     }
// }
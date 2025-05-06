using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Schema.Identity;
using Marten.Storage;

namespace Skeleton.Endpoints;

public record PlaceOrderRequest(List<OrderItem> OrderItems);

public record OrderItem(Guid ItemId, uint Quantity);

public abstract class OrderEndpoints : IEndpoint
{
    public static void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/orders", async (PlaceOrderRequest request, IDocumentSession session) =>
            {
                if (request.OrderItems.Count == 0)
                    return Results.BadRequest("Order must contain at least one item.");

                var insufficient = new List<Guid>();

                foreach (var orderItem in request.OrderItems)
                {
                    var item = await session.Events.FetchForWriting<ItemAggregate>(orderItem.ItemId);
                    if (item.Aggregate == null)
                        return Results.BadRequest($"Item with id {orderItem.ItemId} does not exist.");

                    if (item.Aggregate.Quantity < orderItem.Quantity)
                        insufficient.Add(orderItem.ItemId);
                }

                if (insufficient.Count != 0)
                    return Results.BadRequest($"Not enough stock for: {string.Join(", ", insufficient)}");

                var orderId = CombGuidIdGeneration.NewGuid();
                var orderPlaced = new OrderPlaced(orderId, request.OrderItems, DateTime.UtcNow);
                session.Events.StartStream<OrderAggregate>(orderId, orderPlaced);

                foreach (var item in request.OrderItems)
                {
                    session.Events.Append(item.ItemId, new ItemReserved(item.ItemId, item.Quantity));
                }

                await session.SaveChangesAsync();
                return Results.Created($"/orders/{orderId}", new { orderId });
            })
            .WithName("PlaceOrder")
            .WithDescription("Places an order if item is sufficient.")
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest);

        endpoints.MapGet("/orders/", async (IQuerySession session) =>
            {
                var overview = await session.Query<OrderOverview>().ToListAsync(); // TODO: Create  
                return Results.Ok(overview);
            })
            .WithName("GetOrderOverviews")
            .WithDescription("Get orders with item status using a multi-stream projection.")
            .Produces<List<OrderOverview>>()
            .Produces(StatusCodes.Status404NotFound);

        endpoints.MapGet("/orders/{orderId:guid}/overview", async (
                Guid orderId,
                IQuerySession session) =>
            {
                var overview = await session.LoadAsync<OrderOverview>(orderId);
                return overview is null ? Results.NotFound() : Results.Ok(overview);
            })
            .WithName("GetOrderOverview")
            .WithDescription("Gets an order with item status using a multi-stream projection.")
            .Produces<OrderOverview>()
            .Produces(StatusCodes.Status404NotFound);
    }
}

// 📦 Events
public record OrderPlaced(Guid OrderId, List<OrderItem> Items, DateTime PlacedAt);

public record OrderConfirmed(Guid OrderId);

// 🧱 Aggregate
public record OrderAggregate(Guid Id, List<OrderItem> Items, bool IsConfirmed)
{
    public static OrderAggregate Create(OrderPlaced e) => new(e.OrderId, e.Items, false);

    public static OrderAggregate Apply(OrderAggregate current, OrderConfirmed e) => current with { IsConfirmed = true };
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


    public static OrderOverview Apply(OrderOverview view, ItemAdded e)
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

        return view with { Items = updatedItems, EventsApplied = view.EventsApplied + 1 };
    }

    private class OrderSlicer : IEventSlicer<OrderOverview, Guid>
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
                        var orderPlacedEvents = await querySession.Events.QueryRawEventDataOnly<OrderPlaced>()
                            .Where(op => op.Items.Any(i => i.ItemId == itemChanged.Data.ItemId))
                            .ToListAsync();
                        foreach (var orderPlaced in orderPlacedEvents)
                        {
                            var existing = await querySession.LoadAsync<OrderOverview>(orderPlaced.OrderId);
                            if (existing is null)
                            {
                                Console.WriteLine($"[WARN] Missing OrderOverview for OrderId {orderPlaced.OrderId} while handling ItemChangedName for ItemId {itemChanged.Data.ItemId}.");
                                continue;
                            }
                            sliceGroup.AddEvent(orderPlaced.OrderId, itemChanged);
                        }
                        break;
                    }
                }
            }

            return [sliceGroup];
        }
    }
}
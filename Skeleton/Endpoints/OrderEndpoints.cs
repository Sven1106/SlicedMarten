using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Events.Projections.Flattened;
using Marten.Schema.Identity;
using Marten.Storage;

namespace Skeleton.Endpoints;

public record PlaceOrderRequest(List<OrderItem> OrderItems);

public record OrderItem(Guid ItemId, uint Quantity);

public record RemoveItemFromOrderRequest(Guid ItemId);

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

        endpoints.MapPost("/orders/{orderId:guid}/remove-item", async (Guid orderId, RemoveItemFromOrderRequest request, IDocumentSession session) =>
            {
                var stream = await session.Events.FetchForWriting<OrderAggregate>(orderId);
                if (stream.Aggregate is null)
                    return Results.NotFound();
                var order = stream.Aggregate;
                var exists = order.Items.Any(i => i.ItemId == request.ItemId);
                if (!exists)
                    return Results.BadRequest("Item not found in order.");

                session.Events.Append(orderId, new ItemRemovedFromOrder(orderId, request.ItemId));
                await session.SaveChangesAsync();

                return Results.Accepted();
            })
            .WithName("RemoveItemFromOrder")
            .WithDescription("Removes an item from an existing order.")
            .Produces(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);
    }
}

// 📦 Events
public record OrderPlaced(Guid OrderId, List<OrderItem> Items, DateTime PlacedAt);

public record OrderConfirmed(Guid OrderId);

public record ItemRemovedFromOrder(Guid OrderId, Guid ItemId);

// 🧱 Aggregate
public record OrderAggregate(Guid Id, List<OrderItem> Items, bool IsConfirmed)
{
    public static OrderAggregate Create(OrderPlaced e) => new(e.OrderId, e.Items, false);

    public static OrderAggregate Apply(OrderAggregate current, OrderConfirmed e) => current with { IsConfirmed = true };

    public static OrderAggregate Apply(OrderAggregate current, ItemRemovedFromOrder e) =>
        current with { Items = current.Items.Where(orderItem => orderItem.ItemId != e.ItemId).ToList() };
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

    public static OrderOverview Apply(OrderOverview view, ItemReserved e)
    {
        return view with { Items = view.Items, EventsApplied = view.EventsApplied + 1 };
    }

    public static OrderOverview Apply(OrderOverview view, ItemRemovedFromOrder e)
    {
        var updatedItems = view.Items.Where(i => i.ItemId != e.ItemId).ToList();
        return view with
        {
            Items = updatedItems,
            ItemCount = updatedItems.Count,
            EventsApplied = view.EventsApplied + 1
        };
    }

    private class OrderSlicer : IEventSlicer<OrderOverview, Guid>
    {
        public ValueTask<IReadOnlyList<EventSlice<OrderOverview, Guid>>> SliceInlineActions(IQuerySession session, IEnumerable<StreamAction> streams) =>
            throw new NotImplementedException();

        public async ValueTask<IReadOnlyList<TenantSliceGroup<OrderOverview, Guid>>> SliceAsyncEvents(
            IQuerySession querySession,
            List<IEvent> events
            // On Rebuild: Gets all events that are specified with Create and Apply in the projection
            // On SaveChangesAsync: Only gets the events persisted on SaveChangesAsync and specified with Create and Apply in the projection
        )
        {
            // Cases:
            // 1. Rebuild projections
            // 2. OrderPlaced
            // 3. A stream an order is referencing is modified.

            var tenant = Tenant.ForDatabase(querySession.Database);
            var sliceGroup = new TenantSliceGroup<OrderOverview, Guid>(tenant);
            var streamIdToStreamEvents = new Dictionary<Guid, IReadOnlyList<IEvent>>();
            var (createEvents, applyEvents) = events.SplitByTypes(typeof(OrderPlaced));
            //TODO: only add stream events that matches the events specified with Create in the projection
            if (createEvents.Count != 0)
            {
                foreach (var createEvent in createEvents)
                {
                    switch (createEvent)
                    {
                        case IEvent<OrderPlaced> placed:
                        {
                            sliceGroup.AddEvent(placed.Data.OrderId, placed);

                            foreach (var orderItem in placed.Data.Items)
                            {
                                if (streamIdToStreamEvents.TryGetValue(orderItem.ItemId, out var streamEvents) == false) // Reference to other stream
                                {
                                    streamEvents = await querySession.Events.FetchStreamAsync(orderItem.ItemId); // Will this scale?
                                    streamIdToStreamEvents[orderItem.ItemId] = streamEvents;
                                }

                                foreach (var streamEvent in streamIdToStreamEvents[orderItem.ItemId])
                                {
                                    switch (streamEvent)
                                    {
                                        //TODO: only add stream events that matches the events specified with Apply in the projection
                                        case IEvent<ItemAdded>:
                                        case IEvent<ItemChangedName>:
                                        case IEvent<ItemReserved>:
                                            sliceGroup.AddEvent(placed.Data.OrderId, streamEvent);
                                            break;
                                    }
                                }
                            }

                            break;
                        }
                    }
                }
            }
            else
            {
                foreach (var applyEvent in applyEvents)
                {
                    switch (applyEvent)
                    {
                        //TODO: only add stream events that matches the events specified with Apply in the projection
                        case IEvent<ItemChangedName> itemChanged:
                        {
                            foreach (var orderId in (await querySession.LoadAsync<ItemIdToOrderIds>(itemChanged.Data.ItemId))?.OrderIds ?? [])
                            {
                                sliceGroup.AddEvent(orderId, itemChanged);
                            }

                            break;
                        }
                        case IEvent<ItemAdded>:
                        case IEvent<ItemReserved>:
                            break;
                    }
                }
            }

            return [sliceGroup];
        }
    }
}

public static class EventExtensions
{
    public static (List<IEvent> matched, List<IEvent> rest) SplitByTypes(
        this IEnumerable<IEvent> events,
        params Type[] targetTypes)
    {
        var matched = new List<IEvent>();
        var rest = new List<IEvent>();
        var typeSet = new HashSet<Type>(targetTypes);

        foreach (var e in events)
        {
            var dataType = e.GetType().GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEvent<>))
                ?.GetGenericArguments()[0];

            if (dataType != null && typeSet.Contains(dataType))
                matched.Add(e);
            else
                rest.Add(e);
        }

        return (matched, rest);
    }
}

public record ItemIdToOrderIds(Guid Id, List<Guid> OrderIds);

public class ItemToOrdersProjection : MultiStreamProjection<ItemIdToOrderIds, Guid>
{
    public ItemToOrdersProjection() => Identities<OrderPlaced>(e => e.Items.Select(i => i.ItemId).ToList());

    public static ItemIdToOrderIds Create(IEvent<OrderPlaced> e) => new(
        Guid.Empty, // Martern overwrites this behind the scenes with slice-id (ItemId), so it really doesnt matter what is written here.
        [e.Data.OrderId]
    );


    public static ItemIdToOrderIds Apply(ItemIdToOrderIds view, IEvent<OrderPlaced> e)
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
using Marten;
using Marten.Events.Aggregation;

namespace Skeleton.Endpoints;

// 📥 Request models
public record AddItemRequest(string Name, string Description, uint Quantity);

public record CountItemRequest(uint ActualQuantity, string? Reason);

public record ChangeItemNameRequest(string NewName);

public record ReserveItemRequest(uint Quantity);

public abstract class ItemEndpoints : IEndpoint
{
    public static void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/item", async (AddItemRequest request, IDocumentSession session) =>
            {
                if (string.IsNullOrWhiteSpace(request.Name))
                    return Results.BadRequest("Name is required.");

                var itemId = Guid.NewGuid();
                var @event = new ItemAdded(itemId, request.Name, request.Description, request.Quantity);

                session.Events.StartStream<Item>(itemId, @event);
                await session.SaveChangesAsync();

                return Results.Created($"/item/{itemId}", new { itemId });
            })
            .WithName("AddItem")
            .WithDescription("Adds a new item item with the given name, description, and quantity.")
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest);

        endpoints.MapPost("/item/{itemId:guid}/change-name", async (Guid itemId, ChangeItemNameRequest request, IDocumentSession session) =>
            {
                var stream = await session.Events.FetchForWriting<Item>(itemId);
                if (stream.Aggregate is null)
                    return Results.NotFound();

                var @event = new ItemChangedName(itemId, request.NewName);
                session.Events.Append(itemId, @event);
                await session.SaveChangesAsync();

                return Results.Accepted();
            })
            .WithName("ChangeItemName")
            .WithDescription("Change the name of the item item with the given name")
            .Produces(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        endpoints.MapPost("/item/{itemId:guid}/count", async (Guid itemId, CountItemRequest request, IDocumentSession session) =>
            {
                var stream = await session.Events.FetchForWriting<Item>(itemId);
                if (stream.Aggregate is null)
                    return Results.NotFound();

                var @event = new ItemCounted(itemId, request.ActualQuantity, request.Reason);
                session.Events.Append(itemId, @event);
                await session.SaveChangesAsync();

                return Results.Accepted();
            })
            .WithName("CountItem")
            .WithDescription("Sets the actual item quantity for an item, optionally with a reason (e.g. damaged goods, audit result).")
            .Produces(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        endpoints.MapPost("/item/{itemId:guid}/reserve", async (Guid itemId, ReserveItemRequest request, IDocumentSession session) =>
            {
                if (request.Quantity == 0)
                    return Results.BadRequest("Quantity must be greater than zero.");

                var stream = await session.Events.FetchForWriting<Item>(itemId);
                if (stream.Aggregate is null)
                    return Results.NotFound();

                var currentItem = stream.Aggregate;

                if (currentItem.Quantity < request.Quantity)
                    return Results.BadRequest("Not enough item available.");

                var @event = new ItemReserved(itemId, request.Quantity);
                session.Events.Append(itemId, @event);

                await session.SaveChangesAsync();
                return Results.Accepted();
            })
            .WithName("ReserveItem")
            .WithDescription("Reserves quantity of item if available.")
            .Produces(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        endpoints.MapGet("/item", async (IQuerySession query) =>
            {
                var items = await query.Query<ItemSummary>().ToListAsync();
                return Results.Ok(items);
            })
            .WithName("ListItems")
            .WithDescription("Returns a list of all item items for overview.")
            .Produces<List<ItemSummary>>();

        endpoints.MapGet("/item/{itemId:guid}", async (
                Guid itemId,
                IDocumentSession documentSession) =>
            {
                var doc = await documentSession.Events.FetchLatest<ItemDetails>(itemId);
                return doc is null ? Results.NotFound() : Results.Ok(doc);
            })
            .WithName("GetItem")
            .WithDescription("Gets the current projected details of an item item.")
            .Produces<ItemDetails>()
            .Produces(StatusCodes.Status404NotFound);
    }
}

// 📦 Event
public record ItemAdded(Guid ItemId, string Name, string Description, uint Quantity);

public record ItemChangedName(Guid ItemId, string NewName);

public record ItemCounted(Guid ItemId, uint ActualQuantity, string? Reason);

public record ItemReserved(Guid ItemId, uint QuantityReserved);

// Aggregates
public record Item(Guid Id, string Name, uint Quantity)
{
    public static Item Create(ItemAdded e) => new(
        e.ItemId,
        e.Name,
        e.Quantity
    );

    public static Item Apply(Item aggregate, ItemCounted e) => aggregate with
    {
        Quantity = e.ActualQuantity
    };

    public static Item Apply(Item aggregate, ItemChangedName e) => aggregate with
    {
        Name = e.NewName
    };

    public static Item Apply(Item aggregate, ItemReserved e) => aggregate with
    {
        Quantity = aggregate.Quantity - e.QuantityReserved
    };
}

// Projections
public record ItemDetails(Guid Id, string Name, string Description, uint Quantity);

public class ItemDetailsProjection : SingleStreamProjection<ItemDetails>
{
    public static ItemDetails Create(ItemAdded e) => new(
        e.ItemId,
        e.Name,
        e.Description,
        e.Quantity
    );

    public static ItemDetails Apply(ItemDetails view, ItemCounted e) => view with
    {
        Quantity = e.ActualQuantity
    };

    public static ItemDetails Apply(ItemDetails view, ItemChangedName e) => view with
    {
        Name = e.NewName
    };

    public static ItemDetails Apply(ItemDetails view, ItemReserved e) => view with
    {
        Quantity = view.Quantity - e.QuantityReserved
    };
}

public record ItemSummary(Guid Id, string Name, uint Quantity);

public class ItemSummaryProjection : SingleStreamProjection<ItemSummary>
{
    public static ItemSummary Create(ItemAdded e) => new(
        e.ItemId,
        e.Name,
        e.Quantity
    );

    public static ItemSummary Apply(ItemSummary view, ItemCounted e) => view with
    {
        Quantity = e.ActualQuantity
    };

    public static ItemSummary Apply(ItemSummary view, ItemChangedName e) => view with
    {
        Name = e.NewName
    };

    public static ItemSummary Apply(ItemSummary view, ItemReserved e) => view with
    {
        Quantity = view.Quantity - e.QuantityReserved
    };
}
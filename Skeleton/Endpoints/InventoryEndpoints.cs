using Marten;
using Marten.Events.Aggregation;

namespace Skeleton.Endpoints;

// 📥 Request models
public record AddInventoryItemRequest(string Name, string Description, uint Quantity);

public record CountInventoryItemRequest(uint ActualQuantity, string? Reason);

public record ChangeItemNameRequest(string NewName);

public record ReserveInventoryItemRequest(uint Quantity);

public abstract class InventoryEndpoints : IEndpoint
{
    public static void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/inventory", async (AddInventoryItemRequest request, IDocumentSession session) =>
            {
                if (string.IsNullOrWhiteSpace(request.Name))
                    return Results.BadRequest("Name is required.");

                var itemId = Guid.NewGuid();
                var @event = new ItemAddedToInventory(itemId, request.Name, request.Description, request.Quantity);

                session.Events.StartStream<InventoryItem>(itemId, @event);
                await session.SaveChangesAsync();

                return Results.Created($"/inventory/{itemId}", new { itemId });
            })
            .WithName("AddInventoryItem")
            .WithDescription("Adds a new inventory item with the given name, description, and quantity.")
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest);

        endpoints.MapPost("/inventory/{itemId:guid}/change-name", async (Guid itemId, ChangeItemNameRequest request, IDocumentSession session) =>
            {
                var stream = await session.Events.FetchForWriting<InventoryItem>(itemId);
                if (stream.Aggregate is null)
                    return Results.NotFound();

                var @event = new ItemChangedName(itemId, request.NewName);
                session.Events.Append(itemId, @event);
                await session.SaveChangesAsync();

                return Results.Accepted();
            })
            .WithName("ChangeItemName")
            .WithDescription("Change the name of the inventory item with the given name")
            .Produces(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        endpoints.MapPost("/inventory/{itemId:guid}/count", async (Guid itemId, CountInventoryItemRequest request, IDocumentSession session) =>
            {
                var stream = await session.Events.FetchForWriting<InventoryItem>(itemId);
                if (stream.Aggregate is null)
                    return Results.NotFound();

                var @event = new InventoryCounted(itemId, request.ActualQuantity, request.Reason);
                session.Events.Append(itemId, @event);
                await session.SaveChangesAsync();

                return Results.Accepted();
            })
            .WithName("CountInventoryItem")
            .WithDescription("Sets the actual inventory quantity for an item, optionally with a reason (e.g. damaged goods, audit result).")
            .Produces(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        endpoints.MapPost("/inventory/{itemId:guid}/reserve", async (Guid itemId, ReserveInventoryItemRequest request, IDocumentSession session) =>
            {
                if (request.Quantity == 0)
                    return Results.BadRequest("Quantity must be greater than zero.");

                var stream = await session.Events.FetchForWriting<InventoryItem>(itemId);
                if (stream.Aggregate is null)
                    return Results.NotFound();

                var currentInventory = stream.Aggregate;

                if (currentInventory.Quantity < request.Quantity)
                    return Results.BadRequest("Not enough inventory available.");

                var @event = new InventoryReserved(itemId, request.Quantity);
                session.Events.Append(itemId, @event);

                await session.SaveChangesAsync();
                return Results.Accepted();
            })
            .WithName("ReserveInventory")
            .WithDescription("Reserves quantity of inventory if available.")
            .Produces(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        endpoints.MapGet("/inventory", async (IQuerySession query) =>
            {
                var items = await query.Query<InventoryItemSummary>().ToListAsync();
                return Results.Ok(items);
            })
            .WithName("ListInventoryItems")
            .WithDescription("Returns a list of all inventory items for overview.")
            .Produces<List<InventoryItemSummary>>();

        endpoints.MapGet("/inventory/{itemId:guid}", async (
                Guid itemId,
                IDocumentSession documentSession) =>
            {
                var doc = await documentSession.Events.FetchLatest<InventoryItemDetails>(itemId);
                return doc is null ? Results.NotFound() : Results.Ok(doc);
            })
            .WithName("GetInventoryItem")
            .WithDescription("Gets the current projected details of an inventory item.")
            .Produces<InventoryItemDetails>()
            .Produces(StatusCodes.Status404NotFound);
    }
}

// 📦 Event
public record ItemAddedToInventory(Guid ItemId, string Name, string Description, uint Quantity);

public record ItemChangedName(Guid ItemId, string NewName);

public record InventoryCounted(Guid ItemId, uint ActualQuantity, string? Reason);

public record InventoryReserved(Guid ItemId, uint QuantityReserved);

// Aggregates
public record InventoryItem(Guid Id, string Name, uint Quantity)
{
    public static InventoryItem Create(ItemAddedToInventory e)
    {
        return new InventoryItem(e.ItemId, e.Name, e.Quantity);
    }

    public static InventoryItem Apply(InventoryItem aggregate, InventoryCounted e)
    {
        return aggregate with { Quantity = e.ActualQuantity };
    }

    public static InventoryItem Apply(InventoryItem aggregate, ItemChangedName e)
    {
        return aggregate with { Name = e.NewName };
    }

    public static InventoryItem Apply(InventoryItem aggregate, InventoryReserved e)
    {
        return aggregate with { Quantity = aggregate.Quantity - e.QuantityReserved };
    }
}

// Projections
public record InventoryItemDetails(Guid Id, string Name, string Description, uint Quantity);

public class InventoryItemDetailsProjection : SingleStreamProjection<InventoryItemDetails>
{
    public static InventoryItemDetails Create(ItemAddedToInventory e)
    {
        return new InventoryItemDetails(e.ItemId, e.Name, e.Description, e.Quantity);
    }

    public static InventoryItemDetails Apply(InventoryItemDetails view, InventoryCounted e)
    {
        return view with { Quantity = e.ActualQuantity };
    }

    public static InventoryItemDetails Apply(InventoryItemDetails view, ItemChangedName e)
    {
        return view with { Name = e.NewName };
    }

    public static InventoryItemDetails Apply(InventoryItemDetails view, InventoryReserved e)
    {
        return view with { Quantity = view.Quantity - e.QuantityReserved };
    }
}

public record InventoryItemSummary(Guid Id, string Name, uint Quantity);

public class InventoryItemSummaryProjection : SingleStreamProjection<InventoryItemSummary>
{
    public static InventoryItemSummary Create(ItemAddedToInventory e)
    {
        return new InventoryItemSummary(e.ItemId, e.Name, e.Quantity);
    }

    public static InventoryItemSummary Apply(InventoryItemSummary view, InventoryCounted e)
    {
        return view with { Quantity = e.ActualQuantity };
    }

    public static InventoryItemSummary Apply(InventoryItemSummary view, ItemChangedName e)
    {
        return view with { Name = e.NewName };
    }

    public static InventoryItemSummary Apply(InventoryItemSummary view, InventoryReserved e)
    {
        return view with { Quantity = view.Quantity - e.QuantityReserved };
    }
}
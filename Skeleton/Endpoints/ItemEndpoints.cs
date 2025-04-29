using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Schema.Identity;

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

                var itemId = CombGuidIdGeneration.NewGuid();
                var @event = new ItemAdded(itemId, request.Name, request.Description, request.Quantity);

                session.Events.StartStream<ItemAggregate>(itemId, @event);
                await session.SaveChangesAsync();

                return Results.Created($"/item/{itemId}", new { itemId });
            })
            .WithName("AddItem")
            .WithDescription("Adds a new item item with the given name, description, and quantity.")
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest);

        endpoints.MapPost("/item/{itemId:guid}/change-name", async (Guid itemId, ChangeItemNameRequest request, IDocumentSession session) =>
            {
                var stream = await session.Events.FetchForWriting<ItemAggregate>(itemId);
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
                var stream = await session.Events.FetchForWriting<ItemAggregate>(itemId);
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

                var stream = await session.Events.FetchForWriting<ItemAggregate>(itemId);
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

        endpoints.MapGet("/item/{itemId:guid}/changelog", async (
                Guid itemId,
                IDocumentSession session) =>
            {
                var changelog = await session.Events.FetchLatest<ItemChangeLog>(itemId);
                return changelog is null ? Results.NotFound() : Results.Ok(changelog.Entries);
            })
            .WithName("GetItemChangeLog")
            .WithDescription("Gets the changelog for an item showing its history of events.")
            .Produces<List<ItemChangeLog.Entry>>()
            .Produces(StatusCodes.Status404NotFound);
    }
}

// 📦 Event
public record ItemAdded(Guid ItemId, string Name, string Description, uint Quantity);

public record ItemChangedName(Guid ItemId, string NewName);

public record ItemCounted(Guid ItemId, uint ActualQuantity, string? Reason);

public record ItemReserved(Guid ItemId, uint QuantityReserved);

// Aggregates
public record ItemAggregate(Guid Id, string Name, uint Quantity)
{
    public static ItemAggregate Create(ItemAdded e) => new(
        e.ItemId,
        e.Name,
        e.Quantity
    );

    public static ItemAggregate Apply(ItemAggregate aggregate, ItemCounted e) => aggregate with
    {
        Quantity = e.ActualQuantity
    };

    public static ItemAggregate Apply(ItemAggregate aggregate, ItemChangedName e) => aggregate with
    {
        Name = e.NewName
    };

    public static ItemAggregate Apply(ItemAggregate aggregate, ItemReserved e) => aggregate with
    {
        Quantity = aggregate.Quantity - e.QuantityReserved
    };
}

// Projections

#region ItemDetails

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

#endregion

#region ItemSummary

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

#endregion

#region ItemChangeLog

public record ItemChangeLog(Guid Id, List<ItemChangeLog.Entry> Entries)
{
    public record Entry(DateTimeOffset Timestamp, string EventType, List<Entry.FieldChange> Changes)
    {
        public record FieldChange(string FieldName, string? OldValue, string? NewValue);
    }
}

public class ItemChangeLogProjection : SingleStreamProjection<ItemChangeLog>
{
    private static class FieldNames
    {
        public const string Name = nameof(Name);
        public const string Quantity = nameof(Quantity);
    }

    public static ItemChangeLog Create(IEvent<ItemAdded> e)
    {
        return new ItemChangeLog(
            e.StreamId,
            [
                new ItemChangeLog.Entry(
                    e.Timestamp,
                    nameof(ItemAdded),
                    [
                        new ItemChangeLog.Entry.FieldChange(FieldNames.Name, null, e.Data.Name),
                        new ItemChangeLog.Entry.FieldChange(FieldNames.Quantity, null, e.Data.Quantity.ToString())
                    ]
                )
            ]
        );
    }

    public static ItemChangeLog Apply(ItemChangeLog log, IEvent<ItemChangedName> e)
    {
        var entry = new ItemChangeLog.Entry(
            e.Timestamp,
            nameof(ItemChangedName),
            [
                new ItemChangeLog.Entry.FieldChange(FieldNames.Name, log.GetCurrentString(FieldNames.Name), e.Data.NewName)
            ]
        );

        return log with { Entries = [..log.Entries, entry] };
    }

    public static ItemChangeLog Apply(ItemChangeLog log, IEvent<ItemCounted> e)
    {
        var entry = new ItemChangeLog.Entry(
            e.Timestamp,
            nameof(ItemCounted),
            [
                new ItemChangeLog.Entry.FieldChange(FieldNames.Quantity, log.GetCurrentUInt(FieldNames.Quantity).ToString(), e.Data.ActualQuantity.ToString())
            ]
        );

        return log with { Entries = [..log.Entries, entry] };
    }

    public static ItemChangeLog Apply(ItemChangeLog log, IEvent<ItemReserved> e)
    {
        var currentQuantity = log.GetCurrentUInt(FieldNames.Quantity);
        var entry = new ItemChangeLog.Entry(
            e.Timestamp,
            nameof(ItemReserved),
            [
                new ItemChangeLog.Entry.FieldChange(FieldNames.Quantity, currentQuantity.ToString(), (currentQuantity - e.Data.QuantityReserved).ToString())
            ]
        );

        return log with { Entries = [..log.Entries, entry] };
    }
}

public static class ItemChangeLogExtensions
{
    private static T? GetCurrent<T>(this ItemChangeLog log, string fieldName, Func<string, T?> converter)
    {
        for (var i = log.Entries.Count - 1; i >= 0; i--)
        {
            foreach (var change in log.Entries[i].Changes)
            {
                if (change.FieldName == fieldName && change.NewValue is not null)
                    return converter(change.NewValue);
            }
        }

        return default;
    }

    public static string? GetCurrentString(this ItemChangeLog log, string fieldName) => log.GetCurrent<string>(fieldName, s => s);

    public static uint? GetCurrentUInt(this ItemChangeLog log, string fieldName) => log.GetCurrent<uint?>(fieldName, s => uint.TryParse(s, out var v) ? v : null);
}

#endregion
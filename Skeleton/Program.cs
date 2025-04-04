using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Patching;
using Weasel.Core;


var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMarten(opts =>
    {
        opts.Connection(builder.Configuration.GetConnectionString("Marten"));

        // 👇 Projection registration
        opts.Projections.Add<PlayerProfileProjection>(ProjectionLifecycle.Inline);

        opts.AutoCreateSchemaObjects = AutoCreate.All;
    })
    .UseLightweightSessions();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// ✅ Minimal API endpoint

app.MapPost("/players/register", async (string displayName, IDocumentSession session) =>
    {
        var playerId = Guid.NewGuid();
        var @event = new PlayerRegistered(playerId, displayName);

        session.Events.StartStream<PlayerAggregate>(playerId, @event);
        await session.SaveChangesAsync();

        return Results.Ok(new { playerId });
    })
    .WithName("RegisterPlayer")
    .WithDescription("Register a new player with a display name.")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest);

app.MapGet("/players", async (IQuerySession session) =>
    {
        var players = await session.Query<PlayerProfile>().ToListAsync();
        return Results.Ok(players);
    })
    .WithName("GetAllPlayers")
    .WithDescription("Returns all registered players.")
    .Produces<List<PlayerProfile>>();

app.Run();

// 📦 Event
public record PlayerRegistered(Guid PlayerId, string DisplayName);

// 🧱 Aggregate state (immutable record)
public record PlayerAggregate(Guid Id, string DisplayName)
{
    public static PlayerAggregate Create(PlayerRegistered @event) => new(@event.PlayerId, @event.DisplayName);
}

// 👁️ Projection model
public record PlayerProfile
{
    public Guid Id { get; init; }
    public string DisplayName { get; init; }
}

// 👁️ Projection logic
public class PlayerProfileProjection : SingleStreamProjection<PlayerProfile>
{
    public static PlayerProfile Create(PlayerRegistered @event) => new()
    {
        Id = @event.PlayerId,
        DisplayName = @event.DisplayName
    };
}

public class QuestPatchTestProjection : IProjection
{
    public Guid Id { get; set; }

    public string Name { get; set; }

    public void Apply(IDocumentOperations operations, IReadOnlyList<StreamAction> streams)
    {
        var questEvents = streams.SelectMany(x => x.Events).OrderBy(s => s.Sequence).Select(s => s.Data);
    }

    public Task ApplyAsync(IDocumentOperations operations, IReadOnlyList<StreamAction> streams,
        CancellationToken cancellation)
    {
        Apply(operations, streams);
        return Task.CompletedTask;
    }
}
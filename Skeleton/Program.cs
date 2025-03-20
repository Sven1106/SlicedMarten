using Marten;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
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

app.Run();
public record RegisterPlayerRequest(string DisplayName);

// 📦 Event
public record PlayerRegistered(Guid PlayerId, string DisplayName);

// 🧱 Aggregate state (immutable record)
public record PlayerAggregate(Guid Id, string DisplayName)
{
    public static PlayerAggregate Create(PlayerRegistered @event) =>
        new(@event.PlayerId, @event.DisplayName);
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
    public PlayerProfileProjection()
    {
        ProjectEvent<PlayerRegistered>((e, _) =>
            new PlayerProfile
            {
                Id = e.Id,
                DisplayName = e.DisplayName
            });
    }
}
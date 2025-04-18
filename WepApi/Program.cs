using Marten;
using Marten.Events.Projections;
using WepApi.Domain;
using WepApi.Features;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddMarten(opts =>
{
    opts.Connection(builder.Configuration.GetConnectionString("Marten"));
    // Write
    opts.Projections.LiveStreamAggregation<User>();
    // Read
    opts.Projections.Add<GetUserProjection>(ProjectionLifecycle.Inline);
}).UseLightweightSessions();


// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
    {
        var forecast = Enumerable.Range(1, 5).Select(index =>
                new WeatherForecast
                (
                    DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                    Random.Shared.Next(-20, 55),
                    summaries[Random.Shared.Next(summaries.Length)]
                ))
            .ToArray();
        return forecast;
    })
    .WithName("GetWeatherForecast")
    .WithOpenApi();

app.MapGet("/users/{id:guid}", (Guid id) => { })
    .WithName("GetUser")
    .WithOpenApi();

app.MapPost("/users/", async (RegisterUserRequest request, IDocumentSession session) =>
    {
        RegisterUserHandler registerUserHandler = new(session);
        return await registerUserHandler.Handle(request);
    })
    .WithName("RegisterUser")
    .WithOpenApi();

app.Run();

internal record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
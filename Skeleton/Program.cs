using System.Text.Json.Serialization;
using Marten;
using Marten.Events.Daemon.Resiliency;
using Marten.Events.Projections;
using Skeleton;
using Skeleton.Endpoints;
using Weasel.Core;


var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMarten(opts =>
    {
        opts.Connection(builder.Configuration.GetConnectionString("Marten"));
        opts.Projections.Add<InventoryItemDetailsProjection>(ProjectionLifecycle.Inline);
        opts.Projections.Add<InventoryItemSummaryProjection>(ProjectionLifecycle.Inline);
        // opts.Projections.Add<OrderOverviewProjection>(ProjectionLifecycle.Async);
        opts.Projections.Add<UserGroupsAssignmentProjection>(ProjectionLifecycle.Async);

        opts.AutoCreateSchemaObjects = AutoCreate.All;
        opts.Projections.UseIdentityMapForAggregates = true;
    })
    .UseLightweightSessions()
    .AddAsyncDaemon(DaemonMode.Solo);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.ConfigureHttpJsonOptions(options => { options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()); });
builder.Services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(options => { options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()); });

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.MapAllEndpoints();

app.Run();
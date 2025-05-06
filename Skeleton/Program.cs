using System.Reflection;
using System.Text.Json.Serialization;
using JasperFx.CodeGeneration;
using Marten;
using Marten.Events;
using Marten.Events.Daemon.Resiliency;
using Marten.Events.Projections;
using Microsoft.AspNetCore.Mvc;
using Skeleton;
using Skeleton.Endpoints;
using Weasel.Core;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMarten(opts =>
    {
        opts.Connection(builder.Configuration.GetConnectionString("Marten"));
        opts.Projections.Add<ItemDetailsProjection>(ProjectionLifecycle.Inline);
        opts.Projections.Add<ItemSummaryProjection>(ProjectionLifecycle.Inline);
        opts.Projections.Add<ItemChangeLogProjection>(ProjectionLifecycle.Inline);
        opts.Projections.Add<OrderOverviewProjection>(ProjectionLifecycle.Async);
        // opts.Projections.Add<ItemTagUsageProjection>(ProjectionLifecycle.Async);

        // opts.Projections.Add<ItemIdToOrderIdsProjection>(ProjectionLifecycle.Inline);
        // opts.GeneratedCodeMode = TypeLoadMode.Auto;
        opts.AutoCreateSchemaObjects = AutoCreate.All;

        // Force users to supply a stream type on StartStream, and disallow appending events if the stream does not already exist
        opts.Events.UseMandatoryStreamTypeDeclaration = true;

        // Turn on the PostgreSQL table partitioning for hot/cold storage on archived events
        opts.Events.UseArchivedStreamPartitioning = true;

        // Use the *much* faster workflow for appending events at the cost of *some* loss of metadata usage for inline projections
        opts.Events.AppendMode = EventAppendMode.Quick;

        // Little more involved, but this can reduce the number of database queries necessary to process projections during CQRS command handling with certain workflows
        opts.Events.UseIdentityMapForAggregates = true;

        // Opts into a mode where Marten is able to rebuild single stream projections faster by building one stream at a time 
        // Does require new table migrations for Marten 7 users though
        opts.Events.UseOptimizedProjectionRebuilds = true;
    })
    .UseLightweightSessions()
    .AddAsyncDaemon(DaemonMode.Solo);
builder.Services.AddHostedService<ProjectionChangeListener>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.ConfigureHttpJsonOptions(options => { options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()); });
builder.Services.Configure<JsonOptions>(options => { options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()); });

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.MapAllEndpoints();

app.Run();
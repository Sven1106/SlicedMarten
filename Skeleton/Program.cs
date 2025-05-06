using System.Text.Json.Serialization;
using JasperFx.CodeGeneration;
using Marten;
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

        // opts.GeneratedCodeMode = TypeLoadMode.Auto;
        opts.AutoCreateSchemaObjects = AutoCreate.All;
        opts.Projections.UseIdentityMapForAggregates = true;
        opts.Events.UseIdentityMapForAggregates = true;

        // Force users to supply a stream type on StartStream, and disallow
        // appending events if the stream does not already exist
        opts.Events.UseMandatoryStreamTypeDeclaration = true;
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
using Marten;
using SourceGenerator;

namespace Skeleton.Endpoints;

public abstract class AdminEndpoints : IEndpoint
{
    public static void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/admin/rebuild-projections", async (List<ProjectionViewModelEnum> projectionViewModels, CancellationToken cancellationToken, IDocumentStore store) =>
            {
                var daemon = await store.BuildProjectionDaemonAsync();

                foreach (var name in projectionViewModels.Select(projectionViewModel => Enum.GetName(typeof(ProjectionViewModelEnum), projectionViewModel)).OfType<string>())
                {
                    await daemon.RebuildProjectionAsync(name, cancellationToken);
                }

                return Results.Ok(new
                {
                    projectionViewModelsRebuilt = projectionViewModels
                });
            })
            .WithName("RebuildProjections")
            .WithDescription("Rebuilds all inline projections from the event store.")
            .Produces(StatusCodes.Status200OK);

        endpoints.MapPost("/admin/rebuild-stream/{streamId:guid}", async (List<ProjectionViewModelEnum> projections, Guid streamId, IDocumentStore store) =>
            {
                try
                {
                    await store.Advanced.RebuildSingleStreamAsync<InventoryItemDetails>(streamId);
                    await store.Advanced.RebuildSingleStreamAsync<InventoryItemSummary>(streamId);

                    return Results.Ok(new
                    {
                        streamId,
                        projectionsRebuilt = new[] { nameof(InventoryItemDetails), nameof(InventoryItemSummary) }
                    });
                }
                catch (Exception ex)
                {
                    return Results.BadRequest(new
                    {
                        error = ex.Message,
                        streamId
                    });
                }
            })
            .WithName("RebuildStream")
            .WithDescription("Rebuilds all projections for a specific stream.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest);
    }
}
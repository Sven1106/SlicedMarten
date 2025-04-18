using Marten;

namespace Skeleton.Endpoints;

public abstract class AdminEndpoints : IEndpoint
{
    public static void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/admin/rebuild-projections", async (List<ProjectionEnum> projections, CancellationToken cancellationToken, IDocumentStore store) =>
            {
                var daemon = await store.BuildProjectionDaemonAsync();
                foreach (var projection in projections.Select(projectionViewModel => projectionViewModel.GetProjectionViewModelName()))
                {
                    await daemon.RebuildProjectionAsync(projection, cancellationToken);
                }

                return Results.Ok(new
                {
                    projections
                });
            })
            .WithName("RebuildProjections")
            .WithDescription("Rebuilds all projections from the event store.")
            .Produces(StatusCodes.Status200OK);

        endpoints.MapPost("/admin/rebuild-stream/{streamId:guid}", async (List<SingleStreamProjectionEnum> projections, Guid streamId, IDocumentStore store) =>
            {
                try
                {
                    foreach (var projection in projections)
                    {
                        await projection.RebuildSingleStreamAsync(store, streamId);
                    }

                    return Results.Ok(new
                    {
                        streamId,
                        projections
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
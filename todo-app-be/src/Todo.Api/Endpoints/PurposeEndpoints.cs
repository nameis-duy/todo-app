using Todo.Application.Purposes;

namespace Todo.Api.Endpoints;

public static class PurposeEndpoints
{
    public static void MapPurposes(this WebApplication app)
    {
        var purposes = app.MapGroup("/api/purposes").WithTags("Projects").RequireAuthorization();
        purposes.ProducesValidationProblem().ProducesProblem(401).ProducesProblem(403).ProducesProblem(500).ProducesProblem(503);
        purposes.MapGet("/", async ([AsParameters] PurposeQuery query, IPurposeService service, CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.QueryAsync(query, cancellationToken)))
            .WithName("QueryPurposes").WithSummary("List purposes with live task counts and completion progress.");
        purposes.MapGet("/{id:guid}", async (Guid id, IPurposeService service, CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.GetAsync(id, cancellationToken)))
            .WithName("GetPurpose").WithSummary("Get purpose details; use GET /api/tasks?purposeId={id} for its paged tasks.")
            .ProducesProblem(404);
        purposes.MapPost("/", async (SavePurposeRequest request, IPurposeService service, CancellationToken cancellationToken) =>
        {
            var purpose = await service.CreateAsync(request, cancellationToken);
            return TypedResults.Created($"/api/purposes/{purpose.Id}", purpose);
        }).WithName("CreatePurpose").WithSummary("Create a purpose.");
        purposes.MapPut("/{id:guid}", async (Guid id, SavePurposeRequest request, IPurposeService service, CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.UpdateAsync(id, request, cancellationToken)))
            .WithName("UpdatePurpose").WithSummary("Edit a purpose using its current version.").ProducesProblem(404).ProducesProblem(409);
        purposes.MapDelete("/{id:guid}", async (Guid id, Guid version, IPurposeService service, CancellationToken cancellationToken) =>
        {
            await service.DeleteAsync(id, version, cancellationToken);
            return TypedResults.NoContent();
        }).WithName("DeletePurpose").WithSummary("Delete a purpose and preserve its tasks as uncategorized.")
            .ProducesProblem(404).ProducesProblem(409);
    }
}
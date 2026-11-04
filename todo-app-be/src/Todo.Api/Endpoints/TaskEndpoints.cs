using Todo.Application.Tasks;
using Todo.Application.Security;

namespace Todo.Api.Endpoints;

public static class TaskEndpoints
{
    public static void MapTasks(this WebApplication app)
    {
        var tasks = app.MapGroup("/api/tasks").WithTags("Tasks").RequireAuthorization();
        tasks.ProducesValidationProblem().ProducesProblem(401).ProducesProblem(403).ProducesProblem(500).ProducesProblem(503);

        tasks.MapGet("/", async ([AsParameters] TaskQuery query, ITaskService service,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.QueryAsync(query, cancellationToken)))
            .WithName("QueryTasks").WithSummary("Search, filter, sort, and paginate tasks.");

        tasks.MapGet("/{id:guid}", async (Guid id, ITaskService service, CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.GetAsync(id, cancellationToken)))
            .WithName("GetTask").WithSummary("Get task details and its current version.").ProducesProblem(404);

        tasks.MapGet("/{id:guid}/shares", async (Guid id, ITaskService service, ITaskGrants grants, CancellationToken cancellationToken) =>
        {
            if (!(await service.GetAsync(id, cancellationToken)).IsOwner) throw new AccessDeniedException();
            return TypedResults.Ok(await grants.ListAsync(id, cancellationToken));
        }).RequireAuthorization("registered-account").WithSummary("List task sharing grants (registered owner only).").ProducesProblem(403).ProducesProblem(404);

        tasks.MapPut("/{id:guid}/shares", async (Guid id, ShareTaskRequest request, ITaskService service, ITaskGrants grants, CancellationToken cancellationToken) =>
        {
            if (!(await service.GetAsync(id, cancellationToken)).IsOwner) throw new AccessDeniedException();
            await grants.GrantAsync(id, request, cancellationToken);
            return TypedResults.NoContent();
        }).RequireAuthorization("registered-account").WithSummary("Invite a registered account with view or edit access (owner only).").ProducesProblem(403).ProducesProblem(404);

        tasks.MapDelete("/{id:guid}/shares/{userId:guid}", async (Guid id, Guid userId, ITaskService service, ITaskGrants grants, CancellationToken cancellationToken) =>
        {
            if (!(await service.GetAsync(id, cancellationToken)).IsOwner) throw new AccessDeniedException();
            await grants.RevokeAsync(id, userId, cancellationToken);
            return TypedResults.NoContent();
        }).RequireAuthorization("registered-account").WithSummary("Revoke a task sharing grant (owner only).").ProducesProblem(403).ProducesProblem(404);

        tasks.MapPost("/", async (SaveTaskRequest request, ITaskService service,
            CancellationToken cancellationToken) =>
        {
            var task = await service.CreateAsync(request, cancellationToken);
            return TypedResults.Created($"/api/tasks/{task.Id}", task);
        }).WithName("CreateTask").WithSummary("Create a task.");

        tasks.MapPut("/{id:guid}", async (Guid id, SaveTaskRequest request, ITaskService service,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.UpdateAsync(id, request, cancellationToken)))
            .WithName("UpdateTask").WithSummary("Replace task fields using the current version.")
            .ProducesProblem(404).ProducesProblem(409);

        tasks.MapPatch("/{id:guid}/completion", async (Guid id, SetCompletionRequest request,
            ITaskService service, CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.SetCompletionAsync(id, request, cancellationToken)))
            .WithName("SetTaskCompletion").WithSummary("Complete a task or reopen it as pending.")
            .ProducesProblem(404).ProducesProblem(409);

        tasks.MapDelete("/{id:guid}", async (Guid id, Guid version, ITaskService service,
            CancellationToken cancellationToken) =>
        {
            await service.DeleteAsync(id, version, cancellationToken);
            return TypedResults.NoContent();
        }).WithName("DeleteTask").WithSummary("Permanently delete a task using its current version query parameter.")
            .ProducesProblem(404).ProducesProblem(409);
    }
}
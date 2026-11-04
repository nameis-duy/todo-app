using FluentValidation;
using FluentValidation.Results;
using Todo.Application.Purposes;
using Todo.Domain.Tasks;
using Todo.Application.Security;

namespace Todo.Application.Tasks;

public interface ITaskService
{
    Task<TaskPage> QueryAsync(TaskQuery query, CancellationToken cancellationToken);
    Task<TaskResponse> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<TaskResponse> CreateAsync(SaveTaskRequest request, CancellationToken cancellationToken);
    Task<TaskResponse> UpdateAsync(Guid id, SaveTaskRequest request, CancellationToken cancellationToken);
    Task<TaskResponse> SetCompletionAsync(Guid id, SetCompletionRequest request, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, Guid version, CancellationToken cancellationToken);
}

public sealed class TaskService(ITaskRepository repository, IValidator<SaveTaskRequest> saveValidator,
    IValidator<TaskQuery> queryValidator, IPurposeRepository purposes, TimeProvider clock,
    ICurrentUser currentUser, ITaskGrants grants) : ITaskService
{
    public async Task<TaskPage> QueryAsync(TaskQuery query, CancellationToken cancellationToken)
    {
        await queryValidator.ValidateAndThrowAsync(query, cancellationToken);
        return await repository.QueryAsync(query, cancellationToken);
    }

    public async Task<TaskResponse> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await RespondAsync(await FindAsync(id, cancellationToken), cancellationToken);

    public async Task<TaskResponse> CreateAsync(SaveTaskRequest request, CancellationToken cancellationToken)
    {
        await saveValidator.ValidateAndThrowAsync(request, cancellationToken);
        await ValidatePurposeAsync(request.PurposeId, cancellationToken);
        var task = TodoTask.Create(request.Title, request.Description, request.Status, request.Priority,
            request.StartDate, request.DueDate, clock.GetUtcNow(), request.PurposeId, currentUser.Id);
        repository.Add(task);
        await repository.SaveChangesAsync(cancellationToken);
        return await RespondAsync(task, cancellationToken);
    }

    public async Task<TaskResponse> UpdateAsync(Guid id, SaveTaskRequest request, CancellationToken cancellationToken)
    {
        await saveValidator.ValidateAndThrowAsync(request, cancellationToken);
        ValidateVersion(request.Version);
        var task = await FindAsync(id, cancellationToken);
        await EnsureEditableAsync(task, cancellationToken);
        if (task.OwnerId == currentUser.Id) await ValidatePurposeAsync(request.PurposeId, cancellationToken);
        else if (request.PurposeId != task.PurposeId) throw new AccessDeniedException();
        CheckVersion(task, request.Version!.Value);
        task.Update(request.Title, request.Description, request.Status, request.Priority,
            request.StartDate, request.DueDate, clock.GetUtcNow(), request.PurposeId);
        await repository.SaveChangesAsync(cancellationToken);
        return await RespondAsync(task, cancellationToken);
    }

    public async Task<TaskResponse> SetCompletionAsync(Guid id, SetCompletionRequest request,
        CancellationToken cancellationToken)
    {
        ValidateVersion(request.Version);
        var task = await FindAsync(id, cancellationToken);
        await EnsureEditableAsync(task, cancellationToken);
        CheckVersion(task, request.Version);
        task.SetCompletion(request.Completed, clock.GetUtcNow());
        await repository.SaveChangesAsync(cancellationToken);
        return await RespondAsync(task, cancellationToken);
    }

    public async Task DeleteAsync(Guid id, Guid version, CancellationToken cancellationToken)
    {
        ValidateVersion(version);
        var task = await FindAsync(id, cancellationToken);
        if (task.OwnerId != currentUser.Id) throw new AccessDeniedException();
        CheckVersion(task, version);
        await grants.ClearAsync(id, cancellationToken);
        repository.Remove(task);
        await repository.SaveChangesAsync(cancellationToken);
    }

    private async Task<TodoTask> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await repository.FindAsync(id, cancellationToken) ?? throw new TaskNotFoundException();

    private async Task EnsureEditableAsync(TodoTask task, CancellationToken cancellationToken)
    {
        if (task.OwnerId != currentUser.Id && !await grants.CanEditAsync(task.Id, cancellationToken))
            throw new AccessDeniedException();
    }

    private static void ValidateVersion(Guid? version)
    {
        if (version is null || version == Guid.Empty)
            throw new ValidationException([new ValidationFailure("Version", "A non-empty current version is required.")]);
    }

    private static void CheckVersion(TodoTask task, Guid version)
    {
        if (task.Version != version) throw new TaskConflictException();
    }

    private async Task ValidatePurposeAsync(Guid? id, CancellationToken cancellationToken)
    {
        if (id.HasValue && await purposes.FindAsync(id.Value, cancellationToken) is null)
            throw new InvalidPurposeException();
    }

    private async Task<TaskResponse> RespondAsync(TodoTask task, CancellationToken cancellationToken)
    {
        var purpose = task.PurposeId.HasValue ? await purposes.FindAsync(task.PurposeId.Value, cancellationToken) : null;
        return new(task.Id, task.Title, task.Description, task.Status, task.Priority, task.StartDate,
            task.DueDate, task.CompletedAt, task.CreatedAt, task.UpdatedAt, task.Version, task.PurposeId, purpose?.Title,
            task.OwnerId, task.OwnerId == currentUser.Id,
            task.OwnerId == currentUser.Id || await grants.CanEditAsync(task.Id, cancellationToken));
    }
}
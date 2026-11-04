namespace Todo.Application.Security;

public sealed record TaskShareResponse(Guid UserId, string Email, bool CanEdit);
public sealed record ShareTaskRequest(string Email, bool CanEdit = false);

public interface ITaskGrants
{
    Task<bool> CanViewAsync(Guid taskId, CancellationToken cancellationToken);
    Task<bool> CanEditAsync(Guid taskId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Guid>> SharedIdsAsync(CancellationToken cancellationToken, bool editOnly = false);
    Task<IReadOnlyList<TaskShareResponse>> ListAsync(Guid taskId, CancellationToken cancellationToken);
    Task GrantAsync(Guid taskId, ShareTaskRequest request, CancellationToken cancellationToken);
    Task RevokeAsync(Guid taskId, Guid userId, CancellationToken cancellationToken);
    Task ClearAsync(Guid taskId, CancellationToken cancellationToken);
}
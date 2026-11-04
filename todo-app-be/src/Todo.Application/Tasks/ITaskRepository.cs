using Todo.Domain.Tasks;

namespace Todo.Application.Tasks;

public interface ITaskRepository
{
    Task<TaskPage> QueryAsync(TaskQuery query, CancellationToken cancellationToken);
    Task<TodoTask?> FindAsync(Guid id, CancellationToken cancellationToken);
    void Add(TodoTask task);
    void Remove(TodoTask task);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed class TaskNotFoundException : Exception;
public sealed class TaskConflictException : Exception;
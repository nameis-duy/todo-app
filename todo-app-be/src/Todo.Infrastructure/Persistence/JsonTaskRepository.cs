using Todo.Application.Tasks;
using Todo.Domain.Tasks;
using Todo.Application.Security;

namespace Todo.Infrastructure.Persistence;

public sealed class JsonTaskRepository(JsonTaskStore store, ICurrentUser currentUser, ITaskGrants grants) : ITaskRepository
{
    private TodoTask? trackedTask;
    private Guid? originalVersion;
    private bool delete;

    public async Task<TaskPage> QueryAsync(TaskQuery query, CancellationToken cancellationToken)
    {
        var snapshot = await store.ReadSnapshotAsync(cancellationToken);
        var sharedIds = (await grants.SharedIdsAsync(cancellationToken)).ToHashSet();
        var editableIds = (await grants.SharedIdsAsync(cancellationToken, editOnly: true)).ToHashSet();
        var titles = snapshot.Purposes.ToDictionary(purpose => purpose.Id, purpose => purpose.Title);
        IEnumerable<TodoTask> tasks = snapshot.Tasks.Where(task => task.OwnerId == currentUser.Id || sharedIds.Contains(task.Id));
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            tasks = tasks.Where(task => task.Title.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (task.Description?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));
        }
        if (query.Status.HasValue) tasks = tasks.Where(task => task.Status == query.Status);
        if (query.Priority.HasValue) tasks = tasks.Where(task => task.Priority == query.Priority);
        if (query.PurposeId.HasValue) tasks = tasks.Where(task => task.PurposeId == query.PurposeId);
        if (query.Uncategorized) tasks = tasks.Where(task => task.PurposeId == null);
        if (query.DueFrom.HasValue) tasks = tasks.Where(task => task.DueDate >= query.DueFrom);
        if (query.DueTo.HasValue) tasks = tasks.Where(task => task.DueDate <= query.DueTo);

        var filtered = tasks.ToList();
        IOrderedEnumerable<TodoTask> ordered = query.SortBy switch
        {
            "title" => Order(filtered, task => task.Title, query.Descending),
            "priority" => Order(filtered, task => task.Priority, query.Descending),
            "updatedAt" => Order(filtered, task => task.UpdatedAt, query.Descending),
            "dueDate" => query.Descending
                ? filtered.OrderByDescending(task => !task.DueDate.HasValue).ThenByDescending(task => task.DueDate)
                : filtered.OrderBy(task => !task.DueDate.HasValue).ThenBy(task => task.DueDate),
            _ => Order(filtered, task => task.CreatedAt, query.Descending)
        };
        var items = ordered.ThenBy(task => task.Id).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Select(task => new TaskResponse(task.Id, task.Title, task.Description, task.Status,
                task.Priority, task.StartDate, task.DueDate, task.CompletedAt, task.CreatedAt,
                task.UpdatedAt, task.Version, task.PurposeId,
                task.OwnerId == currentUser.Id && task.PurposeId.HasValue ? titles.GetValueOrDefault(task.PurposeId.Value) : null, task.OwnerId,
                task.OwnerId == currentUser.Id, task.OwnerId == currentUser.Id || editableIds.Contains(task.Id))).ToList();
        return new TaskPage(items, filtered.Count, query.Page, query.PageSize);
    }

    public async Task<TodoTask?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        trackedTask = (await store.ReadAsync(cancellationToken)).SingleOrDefault(task => task.Id == id);
        if (trackedTask is not null && trackedTask.OwnerId != currentUser.Id && !await grants.CanViewAsync(id, cancellationToken))
            trackedTask = null;
        originalVersion = trackedTask?.Version;
        delete = false;
        return trackedTask;
    }

    public void Add(TodoTask task)
    {
        trackedTask = task;
        originalVersion = null;
        delete = false;
    }

    public void Remove(TodoTask task)
    {
        if (trackedTask != task || originalVersion is null)
            throw new InvalidOperationException("Load a task before removing it.");
        delete = true;
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        if (trackedTask is null) return;
        await store.CommitAsync(trackedTask, originalVersion, delete, cancellationToken);
        originalVersion = trackedTask.Version;
        if (delete) trackedTask = null;
    }

    private static IOrderedEnumerable<TodoTask> Order<TKey>(IEnumerable<TodoTask> tasks,
        Func<TodoTask, TKey> key, bool descending) =>
        descending ? tasks.OrderByDescending(key) : tasks.OrderBy(key);
}
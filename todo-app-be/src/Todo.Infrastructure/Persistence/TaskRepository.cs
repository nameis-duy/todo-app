using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Todo.Application.Tasks;
using Todo.Domain.Tasks;
using Todo.Application.Purposes;
using Microsoft.Data.SqlClient;
using Todo.Application.Security;

namespace Todo.Infrastructure.Persistence;

public sealed class TaskRepository(TodoDbContext database, ICurrentUser currentUser) : ITaskRepository
{
    private IQueryable<TodoTask> Accessible => database.Tasks.Where(task => task.OwnerId == currentUser.Id ||
        database.TaskShares.Any(share => share.Storage == "sql" && share.TaskId == task.Id && share.UserId == currentUser.Id));

    private Expression<Func<TodoTask, TaskResponse>> Projection => task =>
        new TaskResponse(task.Id, task.Title, task.Description, task.Status, task.Priority,
            task.StartDate, task.DueDate, task.CompletedAt, task.CreatedAt, task.UpdatedAt, task.Version,
            task.PurposeId, task.OwnerId != currentUser.Id || task.Purpose == null ? null : task.Purpose.Title, task.OwnerId, task.OwnerId == currentUser.Id,
            task.OwnerId == currentUser.Id || database.TaskShares.Any(share => share.Storage == "sql" &&
                share.TaskId == task.Id && share.UserId == currentUser.Id && share.CanEdit));

    public async Task<TaskPage> QueryAsync(TaskQuery query, CancellationToken cancellationToken)
    {
        var tasks = Accessible.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            tasks = tasks.Where(task => task.Title.ToLower().Contains(search.ToLower()) ||
                (task.Description != null && task.Description.ToLower().Contains(search.ToLower())));
        }
        if (query.Status.HasValue) tasks = tasks.Where(task => task.Status == query.Status);
        if (query.Priority.HasValue) tasks = tasks.Where(task => task.Priority == query.Priority);
        if (query.PurposeId.HasValue) tasks = tasks.Where(task => task.PurposeId == query.PurposeId);
        if (query.Uncategorized) tasks = tasks.Where(task => task.PurposeId == null);
        if (query.DueFrom.HasValue) tasks = tasks.Where(task => task.DueDate >= query.DueFrom);
        if (query.DueTo.HasValue) tasks = tasks.Where(task => task.DueDate <= query.DueTo);

        var count = await tasks.CountAsync(cancellationToken);
        IOrderedQueryable<TodoTask> ordered = query.SortBy switch
        {
            "title" => Order(tasks, task => task.Title, query.Descending),
            "dueDate" => query.Descending
                ? tasks.OrderByDescending(task => !task.DueDate.HasValue).ThenByDescending(task => task.DueDate)
                : tasks.OrderBy(task => !task.DueDate.HasValue).ThenBy(task => task.DueDate),
            "priority" => Order(tasks, task => task.Priority == TaskPriority.Urgent ? 3 :
                task.Priority == TaskPriority.High ? 2 : task.Priority == TaskPriority.Normal ? 1 : 0, query.Descending),
            "updatedAt" => Order(tasks, task => task.UpdatedAt, query.Descending),
            _ => Order(tasks, task => task.CreatedAt, query.Descending)
        };
        var items = await ordered.ThenBy(task => task.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Select(Projection).ToListAsync(cancellationToken);
        return new TaskPage(items, count, query.Page, query.PageSize);
    }

    public Task<TodoTask?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        Accessible.SingleOrDefaultAsync(task => task.Id == id, cancellationToken);

    public void Add(TodoTask task) => database.Tasks.Add(task);
    public void Remove(TodoTask task) => database.Tasks.Remove(task);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new TaskConflictException();
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 547 })
        {
            throw new InvalidPurposeException();
        }
    }

    private static IOrderedQueryable<TodoTask> Order<TKey>(IQueryable<TodoTask> tasks,
        Expression<Func<TodoTask, TKey>> key, bool descending) =>
        descending ? tasks.OrderByDescending(key) : tasks.OrderBy(key);
}
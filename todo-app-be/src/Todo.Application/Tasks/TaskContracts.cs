using Todo.Domain.Tasks;

namespace Todo.Application.Tasks;

/// <summary>Fields used to create or replace a task. Dates are calendar dates, not instants.</summary>
public sealed record SaveTaskRequest
{
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public TodoStatus Status { get; init; } = TodoStatus.Pending;
    public TaskPriority Priority { get; init; } = TaskPriority.Normal;
    public DateOnly? StartDate { get; init; }
    public DateOnly? DueDate { get; init; }
    public Guid? Version { get; init; }
    public Guid? PurposeId { get; init; }
}

/// <summary>Completes a task or reopens it as pending using its current version.</summary>
public sealed record SetCompletionRequest
{
    public required bool Completed { get; init; }
    public required Guid Version { get; init; }
}

/// <summary>A task with its audit timestamps and optimistic concurrency token.</summary>
public sealed record TaskResponse(Guid Id, string Title, string? Description,
    TodoStatus Status, TaskPriority Priority, DateOnly? StartDate, DateOnly? DueDate,
    DateTimeOffset? CompletedAt, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, Guid Version,
    Guid? PurposeId = null, string? PurposeTitle = null, Guid? OwnerId = null, bool IsOwner = false, bool CanEdit = false);

/// <summary>Bounded task query; sortBy accepts createdAt, updatedAt, title, dueDate, or priority.</summary>
public sealed record TaskQuery(
    string? Search = null,
    TodoStatus? Status = null,
    TaskPriority? Priority = null,
    DateOnly? DueFrom = null,
    DateOnly? DueTo = null,
    string SortBy = "createdAt",
    bool Descending = true,
    int Page = 1,
    int PageSize = 20,
    Guid? PurposeId = null,
    bool Uncategorized = false);

/// <summary>A page of task results and the total number matching the query.</summary>
public sealed record TaskPage(IReadOnlyList<TaskResponse> Items, int TotalCount, int Page, int PageSize);
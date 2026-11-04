namespace Todo.Domain.Tasks;

public enum TodoStatus { Pending, InProgress, Completed }
public enum TaskPriority { Low, Normal, High, Urgent }

public sealed class TodoTask
{
    private TodoTask() { }

    public Guid Id { get; private set; }
    public Guid? OwnerId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public TodoStatus Status { get; private set; }
    public TaskPriority Priority { get; private set; }
    public DateOnly? StartDate { get; private set; }
    public DateOnly? DueDate { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid Version { get; private set; }
    public Guid? PurposeId { get; private set; }
    public Todo.Domain.Purposes.Purpose? Purpose { get; private set; }

    public static TodoTask Create(string title, string? description, TodoStatus status,
        TaskPriority priority, DateOnly? startDate, DateOnly? dueDate, DateTimeOffset now, Guid? purposeId = null, Guid? ownerId = null)
    {
        if (ownerId == Guid.Empty) throw new ArgumentException("Owner ID cannot be empty.");
        var task = new TodoTask { Id = Guid.NewGuid(), CreatedAt = now, OwnerId = ownerId };
        task.Update(title, description, status, priority, startDate, dueDate, now, purposeId);
        return task;
    }

    public static TodoTask Restore(Guid id, string title, string? description, TodoStatus status,
        TaskPriority priority, DateOnly? startDate, DateOnly? dueDate, DateTimeOffset? completedAt,
        DateTimeOffset createdAt, DateTimeOffset updatedAt, Guid version, Guid? purposeId = null, Guid? ownerId = null)
    {
        if (id == Guid.Empty || version == Guid.Empty || createdAt == default || updatedAt < createdAt ||
            (status == TodoStatus.Completed) != completedAt.HasValue ||
            (completedAt.HasValue && (completedAt < createdAt || completedAt > updatedAt)))
            throw new ArgumentException("Stored task metadata is invalid.");

        var task = Create(title, description, status, priority, startDate, dueDate, createdAt.ToUniversalTime(), purposeId, ownerId);
        task.Id = id;
        task.CompletedAt = completedAt?.ToUniversalTime();
        task.UpdatedAt = updatedAt.ToUniversalTime();
        task.Version = version;
        return task;
    }

    public void Update(string title, string? description, TodoStatus status,
        TaskPriority priority, DateOnly? startDate, DateOnly? dueDate, DateTimeOffset now, Guid? purposeId = null)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 200)
            throw new ArgumentException("Title must contain between 1 and 200 characters.");
        if (description?.Length > 4000)
            throw new ArgumentException("Description must not exceed 4000 characters.");
        if (!Enum.IsDefined(status) || !Enum.IsDefined(priority))
            throw new ArgumentException("Status or priority is invalid.");
        if (startDate.HasValue && dueDate.HasValue && dueDate < startDate)
            throw new ArgumentException("Due date cannot precede start date.");
        if (purposeId == Guid.Empty) throw new ArgumentException("Purpose ID cannot be empty.");

        Title = title.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Priority = priority;
        StartDate = startDate;
        DueDate = dueDate;
        PurposeId = purposeId;
        SetStatus(status, now);
    }

    public void ClearPurpose(DateTimeOffset now)
    {
        PurposeId = null;
        UpdatedAt = now;
        Version = Guid.NewGuid();
    }

    public void SetCompletion(bool completed, DateTimeOffset now) =>
        SetStatus(completed ? TodoStatus.Completed : TodoStatus.Pending, now);

    private void SetStatus(TodoStatus status, DateTimeOffset now)
    {
        CompletedAt = status == TodoStatus.Completed ? CompletedAt ?? now : null;
        Status = status;
        UpdatedAt = now;
        Version = Guid.NewGuid();
    }
}
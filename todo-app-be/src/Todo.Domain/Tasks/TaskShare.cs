namespace Todo.Domain.Tasks;

public sealed class TaskShare
{
    public Guid TaskId { get; set; }
    public Guid UserId { get; set; }
    public string Storage { get; set; } = string.Empty;
    public bool CanEdit { get; set; }
}
namespace Todo.Domain.Purposes;

public sealed class Purpose
{
    private Purpose() { }

    public Guid Id { get; private set; }
    public Guid? OwnerId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid Version { get; private set; }

    public static Purpose Create(string title, string? description, DateTimeOffset now, Guid? ownerId = null)
    {
        if (ownerId == Guid.Empty) throw new ArgumentException("Owner ID cannot be empty.");
        var purpose = new Purpose { Id = Guid.NewGuid(), CreatedAt = now, OwnerId = ownerId };
        purpose.Update(title, description, now);
        return purpose;
    }

    public static Purpose Restore(Guid id, string title, string? description,
        DateTimeOffset createdAt, DateTimeOffset updatedAt, Guid version, Guid? ownerId = null)
    {
        if (id == Guid.Empty || version == Guid.Empty || createdAt == default || updatedAt < createdAt)
            throw new ArgumentException("Stored purpose metadata is invalid.");
        var purpose = Create(title, description, createdAt.ToUniversalTime(), ownerId);
        purpose.Id = id;
        purpose.UpdatedAt = updatedAt.ToUniversalTime();
        purpose.Version = version;
        return purpose;
    }

    public void Update(string title, string? description, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 200)
            throw new ArgumentException("Purpose title must contain between 1 and 200 characters.");
        if (description?.Length > 4000)
            throw new ArgumentException("Purpose description must not exceed 4000 characters.");
        Title = title.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        UpdatedAt = now;
        Version = Guid.NewGuid();
    }
}
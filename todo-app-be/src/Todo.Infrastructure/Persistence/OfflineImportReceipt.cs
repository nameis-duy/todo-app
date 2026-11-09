namespace Todo.Infrastructure.Persistence;

public sealed class OfflineImportReceipt
{
    public Guid AccountId { get; set; }
    public Guid BatchId { get; set; }
    public string PayloadHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
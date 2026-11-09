namespace Todo.Application.Security;

public static class GuestIdentity
{
    public const string ClaimType = "todo:guest";
}

public sealed record GuestDataSummary(int Tasks, int Projects);

public interface IGuestData
{
    Task<GuestDataSummary> SummaryAsync(Guid guestId, CancellationToken cancellationToken);
    Task<GuestDataSummary> MergeAsync(Guid guestId, Guid accountId, CancellationToken cancellationToken);
    Task<OfflineGuestResult> ImportAsync(OfflineGuestBatch batch, Guid accountId, CancellationToken cancellationToken);
}
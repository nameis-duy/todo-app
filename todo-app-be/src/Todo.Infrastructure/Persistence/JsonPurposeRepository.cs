using Todo.Application.Purposes;
using Todo.Domain.Purposes;
using Todo.Domain.Tasks;
using Todo.Application.Security;

namespace Todo.Infrastructure.Persistence;

public sealed class JsonPurposeRepository(JsonTaskStore store, ICurrentUser currentUser) : IPurposeRepository
{
    private Purpose? tracked;
    private Guid? originalVersion;

    public async Task<PurposePage> QueryAsync(PurposeQuery query, CancellationToken cancellationToken)
    {
        var snapshot = await store.ReadSnapshotAsync(cancellationToken);
        var counts = snapshot.Tasks.Where(task => task.PurposeId.HasValue).GroupBy(task => task.PurposeId!.Value)
            .ToDictionary(group => group.Key, group => (Total: group.Count(), Completed: group.Count(task => task.Status == TodoStatus.Completed)));
        var owned = snapshot.Purposes.Where(purpose => purpose.OwnerId == currentUser.Id).ToList();
        var items = owned.OrderBy(purpose => purpose.Title).ThenBy(purpose => purpose.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).Select(purpose =>
            {
                var count = counts.GetValueOrDefault(purpose.Id);
                return Map(purpose, count.Total, count.Completed);
            }).ToList();
        return new PurposePage(items, owned.Count, query.Page, query.PageSize);
    }

    public async Task<PurposeResponse?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var snapshot = await store.ReadSnapshotAsync(cancellationToken);
        var purpose = snapshot.Purposes.SingleOrDefault(purpose => purpose.Id == id && purpose.OwnerId == currentUser.Id);
        return purpose is null ? null : Map(purpose, snapshot.Tasks.Count(task => task.PurposeId == id),
            snapshot.Tasks.Count(task => task.PurposeId == id && task.Status == TodoStatus.Completed));
    }

    public async Task<Purpose?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        tracked = (await store.ReadSnapshotAsync(cancellationToken)).Purposes
            .SingleOrDefault(purpose => purpose.Id == id && purpose.OwnerId == currentUser.Id);
        originalVersion = tracked?.Version;
        return tracked;
    }

    public void Add(Purpose purpose) { tracked = purpose; originalVersion = null; }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        if (tracked is null) return;
        await store.CommitPurposeAsync(tracked, originalVersion, false, tracked.UpdatedAt, cancellationToken);
        originalVersion = tracked.Version;
    }

    public Task DeleteAsync(Purpose purpose, DateTimeOffset now, CancellationToken cancellationToken) =>
        store.CommitPurposeAsync(purpose, purpose.Version, true, now, cancellationToken);

    private static PurposeResponse Map(Purpose purpose, int total, int completed) => new(purpose.Id,
        purpose.Title, purpose.Description, purpose.CreatedAt, purpose.UpdatedAt, purpose.Version, total, completed);
}
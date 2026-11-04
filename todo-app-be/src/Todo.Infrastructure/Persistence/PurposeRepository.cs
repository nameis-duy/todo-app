using Microsoft.EntityFrameworkCore;
using Todo.Application.Purposes;
using Todo.Domain.Purposes;
using Todo.Domain.Tasks;
using Todo.Application.Security;

namespace Todo.Infrastructure.Persistence;

public sealed class PurposeRepository(TodoDbContext database, ICurrentUser currentUser) : IPurposeRepository
{
    private IQueryable<Purpose> Owned => database.Purposes.Where(purpose => purpose.OwnerId == currentUser.Id);
    private IQueryable<PurposeResponse> Summaries(IQueryable<Purpose> purposes) => purposes.Select(purpose =>
        new PurposeResponse(purpose.Id, purpose.Title, purpose.Description, purpose.CreatedAt,
            purpose.UpdatedAt, purpose.Version,
            database.Tasks.Count(task => task.PurposeId == purpose.Id),
            database.Tasks.Count(task => task.PurposeId == purpose.Id && task.Status == TodoStatus.Completed)));

    public async Task<PurposePage> QueryAsync(PurposeQuery query, CancellationToken cancellationToken)
    {
        var count = await Owned.CountAsync(cancellationToken);
        var purposes = Owned.AsNoTracking().OrderBy(purpose => purpose.Title).ThenBy(purpose => purpose.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize);
        var items = await Summaries(purposes).ToListAsync(cancellationToken);
        return new PurposePage(items, count, query.Page, query.PageSize);
    }

    public Task<PurposeResponse?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        Summaries(Owned.AsNoTracking().Where(purpose => purpose.Id == id))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<Purpose?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        Owned.SingleOrDefaultAsync(purpose => purpose.Id == id, cancellationToken);

    public void Add(Purpose purpose) => database.Purposes.Add(purpose);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try { await database.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { throw new PurposeConflictException(); }
    }

    public async Task DeleteAsync(Purpose purpose, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var version = Guid.NewGuid();
        await database.Tasks.Where(task => task.PurposeId == purpose.Id).ExecuteUpdateAsync(setters => setters
            .SetProperty(task => task.PurposeId, (Guid?)null)
            .SetProperty(task => task.UpdatedAt, now)
            .SetProperty(task => task.Version, version), cancellationToken);
        database.Purposes.Remove(purpose);
        await SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
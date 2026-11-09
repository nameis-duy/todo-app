using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Todo.Application.Security;

namespace Todo.Infrastructure.Persistence;

public sealed class GuestData(TodoDbContext database, TimeProvider clock,
    IOptions<TaskStorageOptions> options, JsonTaskStore? fileStore = null) : IGuestData
{
    public async Task<GuestDataSummary> SummaryAsync(Guid guestId, CancellationToken cancellationToken)
    {
        if (!await IsGuestAsync(guestId, cancellationToken)) return new(0, 0);
        if (options.Value.UseJsonFile)
        {
            var snapshot = await fileStore!.ReadSnapshotAsync(cancellationToken);
            return new(snapshot.Tasks.Count(task => task.OwnerId == guestId),
                snapshot.Purposes.Count(project => project.OwnerId == guestId));
        }
        var projects = await database.Purposes.CountAsync(project => project.OwnerId == guestId, cancellationToken);
        var tasks = await database.Tasks.CountAsync(task => task.OwnerId == guestId, cancellationToken);
        return new(tasks, projects);
    }

    public async Task<GuestDataSummary> MergeAsync(Guid guestId, Guid accountId, CancellationToken cancellationToken)
    {
        if (guestId == accountId || !await IsGuestAsync(guestId, cancellationToken)
            || await IsGuestAsync(accountId, cancellationToken)
            || !await database.Users.AnyAsync(user => user.Id == accountId, cancellationToken))
            throw new AccessDeniedException();
        var now = clock.GetUtcNow();
        if (options.Value.UseJsonFile)
            return await fileStore!.MergeGuestAsync(guestId, accountId, now, cancellationToken);

        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var projects = await database.Purposes.Where(project => project.OwnerId == guestId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(project => project.OwnerId, accountId)
                .SetProperty(project => project.UpdatedAt, now).SetProperty(project => project.Version, Guid.NewGuid()), cancellationToken);
        var tasks = await database.Tasks.Where(task => task.OwnerId == guestId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(task => task.OwnerId, accountId)
                .SetProperty(task => task.UpdatedAt, now).SetProperty(task => task.Version, Guid.NewGuid()), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(tasks, projects);
    }

    public async Task<OfflineGuestResult> ImportAsync(OfflineGuestBatch batch, Guid accountId, CancellationToken cancellationToken)
    {
        if (await IsGuestAsync(accountId, cancellationToken)
            || !await database.Users.AnyAsync(user => user.Id == accountId, cancellationToken)) throw new AccessDeniedException();
        var now = clock.GetUtcNow();
        var prepared = PreparedGuestImport.Prepare(batch, accountId, now);
        if (options.Value.UseJsonFile)
            return await fileStore!.ImportGuestAsync(batch, prepared, accountId, now, cancellationToken);

        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var receipt = await database.Set<OfflineImportReceipt>().AsNoTracking()
            .SingleOrDefaultAsync(item => item.AccountId == accountId && item.BatchId == batch.BatchId, cancellationToken);
        if (receipt is not null)
        {
            if (receipt.PayloadHash != prepared.Hash) throw new GuestImportConflictException();
            await transaction.CommitAsync(cancellationToken);
            return new(batch.BatchId, batch.Tasks.Count, batch.Purposes.Count);
        }
        var taskIds = prepared.Tasks.Select(task => task.Id).ToArray();
        var purposeIds = prepared.Purposes.Select(purpose => purpose.Id).ToArray();
        if (await database.Tasks.AnyAsync(task => taskIds.Contains(task.Id), cancellationToken)
            || await database.Purposes.AnyAsync(purpose => purposeIds.Contains(purpose.Id), cancellationToken))
            throw new GuestImportConflictException();
        var references = prepared.Tasks.Where(task => task.PurposeId.HasValue).Select(task => task.PurposeId!.Value)
            .Except(purposeIds).Distinct().ToArray();
        if (await database.Purposes.CountAsync(purpose => references.Contains(purpose.Id) && purpose.OwnerId == accountId, cancellationToken) != references.Length)
            throw new Todo.Application.Purposes.InvalidPurposeException();
        database.Purposes.AddRange(prepared.Purposes);
        database.Tasks.AddRange(prepared.Tasks);
        database.Set<OfflineImportReceipt>().Add(new() { AccountId = accountId, BatchId = batch.BatchId, PayloadHash = prepared.Hash, CreatedAt = now });
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(batch.BatchId, batch.Tasks.Count, batch.Purposes.Count);
    }

    private Task<bool> IsGuestAsync(Guid userId, CancellationToken cancellationToken) =>
        database.UserClaims.AnyAsync(claim => claim.UserId == userId
            && claim.ClaimType == GuestIdentity.ClaimType && claim.ClaimValue == "true", cancellationToken);
}
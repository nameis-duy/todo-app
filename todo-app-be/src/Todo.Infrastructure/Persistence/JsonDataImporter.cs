using Microsoft.EntityFrameworkCore;

namespace Todo.Infrastructure.Persistence;

public static class JsonDataImporter
{
    public static async Task<(int Tasks, int Purposes)> ImportAsync(TodoDbContext database,
        JsonTaskStore store, CancellationToken cancellationToken)
    {
        var snapshot = await store.ReadSnapshotAsync(cancellationToken);
        await using var transaction = await database.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, cancellationToken);
        if (await database.Tasks.AnyAsync(cancellationToken) || await database.Purposes.AnyAsync(cancellationToken))
            throw new InvalidOperationException("JSON import requires an empty target database. No data was changed.");
        database.Purposes.AddRange(snapshot.Purposes);
        database.Tasks.AddRange(snapshot.Tasks);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (snapshot.Tasks.Count, snapshot.Purposes.Count);
    }
}
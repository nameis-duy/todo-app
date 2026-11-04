using System.Text.Json;
using System.Text.Json.Serialization;
using Todo.Application.Tasks;
using Todo.Domain.Tasks;
using Todo.Domain.Purposes;
using Todo.Application.Purposes;
using Todo.Application.Security;

namespace Todo.Infrastructure.Persistence;

public sealed class FileStorageException(Exception innerException)
    : Exception("The JSON task store could not be accessed.", innerException);

internal sealed record JsonStoreSnapshot(List<TodoTask> Tasks, List<Purpose> Purposes);

public sealed class JsonTaskStore(string directory) : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        AllowDuplicateProperties = false,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string filePath = Path.Combine(directory, "tasks.json");

    public Task<List<TodoTask>> ReadAsync(CancellationToken cancellationToken) =>
        WithLockAsync(async token => (await ReadCoreAsync(token)).Tasks, cancellationToken);

    internal Task<JsonStoreSnapshot> ReadSnapshotAsync(CancellationToken cancellationToken) =>
        WithLockAsync(ReadCoreAsync, cancellationToken);

    internal Task<GuestDataSummary> MergeGuestAsync(Guid guestId, Guid accountId, DateTimeOffset now,
        CancellationToken cancellationToken) => WithLockAsync(async token =>
    {
        var snapshot = await ReadCoreAsync(token);
        var tasks = 0;
        var projects = 0;
        for (var index = 0; index < snapshot.Purposes.Count; index++)
        {
            var project = snapshot.Purposes[index];
            if (project.OwnerId != guestId) continue;
            snapshot.Purposes[index] = Purpose.Restore(project.Id, project.Title, project.Description,
                project.CreatedAt, now, Guid.NewGuid(), accountId);
            projects++;
        }
        for (var index = 0; index < snapshot.Tasks.Count; index++)
        {
            var task = snapshot.Tasks[index];
            if (task.OwnerId != guestId) continue;
            snapshot.Tasks[index] = TodoTask.Restore(task.Id, task.Title, task.Description, task.Status,
                task.Priority, task.StartDate, task.DueDate, task.CompletedAt, task.CreatedAt, now,
                Guid.NewGuid(), task.PurposeId, accountId);
            tasks++;
        }
        if (tasks > 0 || projects > 0) await WriteCoreAsync(snapshot, token);
        return new GuestDataSummary(tasks, projects);
    }, cancellationToken);

    public Task CommitAsync(TodoTask task, Guid? originalVersion, bool delete, CancellationToken cancellationToken) =>
        WithLockAsync(async token =>
        {
            var snapshot = await ReadCoreAsync(token);
            var tasks = snapshot.Tasks;
            if (!delete && task.PurposeId.HasValue && !snapshot.Purposes.Any(purpose => purpose.Id == task.PurposeId && purpose.OwnerId == task.OwnerId))
                throw new InvalidPurposeException();
            var index = tasks.FindIndex(existing => existing.Id == task.Id);
            if (originalVersion.HasValue)
            {
                if (index < 0 || tasks[index].Version != originalVersion.Value || tasks[index].OwnerId != task.OwnerId)
                    throw new TaskConflictException();
                if (delete) tasks.RemoveAt(index);
                else tasks[index] = task;
            }
            else
            {
                if (index >= 0) throw new TaskConflictException();
                tasks.Add(task);
            }
            await WriteCoreAsync(snapshot, token);
            return true;
        }, cancellationToken);

    internal Task CommitPurposeAsync(Purpose purpose, Guid? originalVersion, bool delete,
        DateTimeOffset now, CancellationToken cancellationToken) => WithLockAsync(async token =>
    {
        var snapshot = await ReadCoreAsync(token);
        var index = snapshot.Purposes.FindIndex(existing => existing.Id == purpose.Id);
        if (originalVersion.HasValue)
        {
            if (index < 0 || snapshot.Purposes[index].Version != originalVersion || snapshot.Purposes[index].OwnerId != purpose.OwnerId)
                throw new PurposeConflictException();
            if (delete)
            {
                snapshot.Purposes.RemoveAt(index);
                foreach (var task in snapshot.Tasks.Where(task => task.PurposeId == purpose.Id)) task.ClearPurpose(now);
            }
            else snapshot.Purposes[index] = purpose;
        }
        else
        {
            if (index >= 0) throw new PurposeConflictException();
            snapshot.Purposes.Add(purpose);
        }
        await WriteCoreAsync(snapshot, token);
        return true;
    }, cancellationToken);

    private async Task<T> WithLockAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(directory);
            using var fileLock = new FileStream(Path.Combine(directory, "tasks.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return await action(cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            throw new FileStorageException(exception);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<JsonStoreSnapshot> ReadCoreAsync(CancellationToken cancellationToken)
    {
        FileStream stream;
        try
        {
            stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                4096, FileOptions.Asynchronous);
        }
        catch (FileNotFoundException)
        {
            var empty = new JsonStoreSnapshot([], []);
            await WriteCoreAsync(empty, cancellationToken);
            return empty;
        }

        await using var dataStream = stream;
        var document = await JsonSerializer.DeserializeAsync<StoredDocument>(stream, JsonOptions, cancellationToken);
        if (document is null || document.SchemaVersion != 1 || document.Tasks is null)
            throw new InvalidDataException("The task file has an unsupported or invalid format.");

        var tasks = new List<TodoTask>(document.Tasks.Count);
        var ids = new HashSet<Guid>();
        foreach (var stored in document.Tasks)
        {
            if (stored is null || !ids.Add(stored.Id))
                throw new InvalidDataException("The task file contains a missing or duplicate task.");
            try
            {
                tasks.Add(TodoTask.Restore(stored.Id, stored.Title, stored.Description, stored.Status,
                    stored.Priority, stored.StartDate, stored.DueDate, stored.CompletedAt,
                    stored.CreatedAt, stored.UpdatedAt, stored.Version, stored.PurposeId, stored.OwnerId));
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException("The task file contains invalid task data.", exception);
            }
        }
        var purposes = new List<Purpose>();
        var purposeIds = new HashSet<Guid>();
        foreach (var stored in document.Purposes ?? [])
        {
            if (stored is null || !purposeIds.Add(stored.Id))
                throw new InvalidDataException("The task file contains a missing or duplicate purpose.");
            try
            {
                purposes.Add(Purpose.Restore(stored.Id, stored.Title, stored.Description,
                    stored.CreatedAt, stored.UpdatedAt, stored.Version, stored.OwnerId));
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException("The task file contains invalid purpose data.", exception);
            }
        }
        if (tasks.Any(task => task.PurposeId.HasValue && !purposes.Any(purpose => purpose.Id == task.PurposeId && purpose.OwnerId == task.OwnerId)))
            throw new InvalidDataException("The task file contains an invalid purpose reference.");
        return new JsonStoreSnapshot(tasks, purposes);
    }

    private async Task WriteCoreAsync(JsonStoreSnapshot snapshot, CancellationToken cancellationToken)
    {
        var temporaryPath = Path.Combine(directory, $"tasks.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                var document = new StoredDocument(1, snapshot.Tasks.Select(task => new TaskResponse(task.Id,
                    task.Title, task.Description, task.Status, task.Priority, task.StartDate, task.DueDate,
                    task.CompletedAt, task.CreatedAt, task.UpdatedAt, task.Version, task.PurposeId, OwnerId: task.OwnerId)).ToList(),
                    snapshot.Purposes.Select(purpose => new StoredPurpose(purpose.Id, purpose.Title,
                        purpose.Description, purpose.CreatedAt, purpose.UpdatedAt, purpose.Version, purpose.OwnerId)).ToList());
                await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    public void Dispose() => gate.Dispose();

    private sealed record StoredDocument(int SchemaVersion, IReadOnlyList<TaskResponse> Tasks,
        IReadOnlyList<StoredPurpose>? Purposes = null);
    private sealed record StoredPurpose(Guid Id, string Title, string? Description,
        DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, Guid Version, Guid? OwnerId = null);
}
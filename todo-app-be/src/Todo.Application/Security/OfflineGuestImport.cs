using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using Todo.Domain.Purposes;
using Todo.Domain.Tasks;

namespace Todo.Application.Security;

/// <summary>A device-local purpose approved for import.</summary>
public sealed record OfflinePurpose(Guid Id, string Title, string? Description, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
/// <summary>A device-local task, including its local purpose reference and original timestamps.</summary>
public sealed record OfflineTask(Guid Id, string Title, string? Description, TodoStatus Status, TaskPriority Priority,
    DateOnly? StartDate, DateOnly? DueDate, DateTimeOffset? CompletedAt, DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt, Guid? PurposeId);
/// <summary>An immutable, retryable batch of at most fifty records; ownership comes from the signed-in actor.</summary>
public sealed record OfflineGuestBatch(Guid BatchId, Guid WorkspaceId, IReadOnlyList<OfflineTask> Tasks, IReadOnlyList<OfflinePurpose> Purposes);
/// <summary>Confirms a durably committed import batch and its record counts.</summary>
public sealed record OfflineGuestResult(Guid BatchId, int Tasks, int Purposes);
public sealed class GuestImportConflictException : Exception;

public sealed record PreparedGuestImport(string Hash, List<TodoTask> Tasks, List<Purpose> Purposes)
{
    public static PreparedGuestImport Prepare(OfflineGuestBatch batch, Guid accountId, DateTimeOffset now)
    {
        try
        {
            if (batch.BatchId == Guid.Empty || batch.WorkspaceId == Guid.Empty || batch.Tasks is null || batch.Purposes is null
                || batch.Tasks.Count + batch.Purposes.Count is < 1 or > 50
                || batch.Tasks.Any(task => task is null) || batch.Purposes.Any(purpose => purpose is null)
                || batch.Tasks.Select(task => task.Id).Distinct().Count() != batch.Tasks.Count
                || batch.Purposes.Select(purpose => purpose.Id).Distinct().Count() != batch.Purposes.Count)
                throw new ArgumentException("Supply a batch ID, workspace ID and 1 to 50 distinct records.");
            Guid Map(Guid id, string kind)
            {
                if (id == Guid.Empty) throw new ArgumentException("Record IDs must not be empty.");
                var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"todo-offline-v1:{accountId:N}:{batch.WorkspaceId:N}:{kind}:{id:N}"));
                return new Guid(hash.AsSpan(0, 16));
            }
            var purposes = batch.Purposes.Select(purpose => Purpose.Restore(Map(purpose.Id, "purpose"), purpose.Title,
                purpose.Description, purpose.CreatedAt, purpose.UpdatedAt, Guid.NewGuid(), accountId)).ToList();
            var tasks = batch.Tasks.Select(task => TodoTask.Restore(Map(task.Id, "task"), task.Title, task.Description,
                task.Status, task.Priority, task.StartDate, task.DueDate, task.CompletedAt, task.CreatedAt, task.UpdatedAt,
                Guid.NewGuid(), task.PurposeId.HasValue ? Map(task.PurposeId.Value, "purpose") : null, accountId)).ToList();
            if (purposes.Any(purpose => purpose.UpdatedAt > now.AddDays(1)) || tasks.Any(task => task.UpdatedAt > now.AddDays(1)))
                throw new ArgumentException("Check the device clock before importing guest work.");
            foreach (var purpose in purposes)
                purpose.Update(purpose.Title, purpose.Description, now > purpose.UpdatedAt ? now : purpose.UpdatedAt);
            foreach (var task in tasks)
                task.Update(task.Title, task.Description, task.Status, task.Priority, task.StartDate, task.DueDate,
                    now > task.UpdatedAt ? now : task.UpdatedAt, task.PurposeId);
            return new(Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(batch))), tasks, purposes);
        }
        catch (ArgumentException exception)
        {
            throw new ValidationException([new ValidationFailure("GuestData", exception.Message)]);
        }
    }
}
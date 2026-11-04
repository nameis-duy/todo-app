using Todo.Domain.Purposes;

namespace Todo.Application.Purposes;

/// <summary>Creates or replaces the editable fields of a purpose.</summary>
public sealed record SavePurposeRequest
{
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public Guid? Version { get; init; }
}

/// <summary>A purpose and its live task completion progress.</summary>
public sealed record PurposeResponse(Guid Id, string Title, string? Description,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, Guid Version, int TotalTasks, int CompletedTasks)
{
    public decimal CompletionPercentage => TotalTasks == 0 ? 0 : Math.Round(100m * CompletedTasks / TotalTasks, 1);
}

/// <summary>Pagination for purposes, bounded to 100 records per page.</summary>
public sealed record PurposeQuery(int Page = 1, int PageSize = 100);

/// <summary>A page of purposes with counts and completion percentages.</summary>
public sealed record PurposePage(IReadOnlyList<PurposeResponse> Items, int TotalCount, int Page, int PageSize);

public interface IPurposeRepository
{
    Task<PurposePage> QueryAsync(PurposeQuery query, CancellationToken cancellationToken);
    Task<PurposeResponse?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<Purpose?> FindAsync(Guid id, CancellationToken cancellationToken);
    void Add(Purpose purpose);
    Task SaveChangesAsync(CancellationToken cancellationToken);
    Task DeleteAsync(Purpose purpose, DateTimeOffset now, CancellationToken cancellationToken);
}

public sealed class PurposeNotFoundException : Exception;
public sealed class PurposeConflictException : Exception;
public sealed class InvalidPurposeException : Exception;
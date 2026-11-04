using FluentValidation;
using FluentValidation.Results;
using Todo.Application.Security;

namespace Todo.Application.Purposes;

public sealed class SavePurposeValidator : AbstractValidator<SavePurposeRequest>
{
    public SavePurposeValidator()
    {
        RuleFor(request => request.Title).NotEmpty().MaximumLength(200);
        RuleFor(request => request.Description).MaximumLength(4000);
    }
}

public interface IPurposeService
{
    Task<PurposePage> QueryAsync(PurposeQuery query, CancellationToken cancellationToken);
    Task<PurposeResponse> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<PurposeResponse> CreateAsync(SavePurposeRequest request, CancellationToken cancellationToken);
    Task<PurposeResponse> UpdateAsync(Guid id, SavePurposeRequest request, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, Guid version, CancellationToken cancellationToken);
}

public sealed class PurposeService(IPurposeRepository repository, IValidator<SavePurposeRequest> validator,
    TimeProvider clock, ICurrentUser currentUser) : IPurposeService
{
    public Task<PurposePage> QueryAsync(PurposeQuery query, CancellationToken cancellationToken)
    {
        var errors = new List<ValidationFailure>();
        if (query.Page is < 1 or > 1_000_000) errors.Add(new("Page", "Page must be between 1 and 1000000."));
        if (query.PageSize is < 1 or > 100) errors.Add(new("PageSize", "Page size must be between 1 and 100."));
        if (errors.Count > 0) throw new ValidationException(errors);
        return repository.QueryAsync(query, cancellationToken);
    }

    public async Task<PurposeResponse> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await repository.GetAsync(id, cancellationToken) ?? throw new PurposeNotFoundException();

    public async Task<PurposeResponse> CreateAsync(SavePurposeRequest request, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(request, cancellationToken);
        var purpose = Domain.Purposes.Purpose.Create(request.Title, request.Description, clock.GetUtcNow(), currentUser.Id);
        repository.Add(purpose);
        await repository.SaveChangesAsync(cancellationToken);
        return await GetAsync(purpose.Id, cancellationToken);
    }

    public async Task<PurposeResponse> UpdateAsync(Guid id, SavePurposeRequest request, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(request, cancellationToken);
        ValidateVersion(request.Version);
        var purpose = await repository.FindAsync(id, cancellationToken) ?? throw new PurposeNotFoundException();
        if (purpose.Version != request.Version) throw new PurposeConflictException();
        purpose.Update(request.Title, request.Description, clock.GetUtcNow());
        await repository.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task DeleteAsync(Guid id, Guid version, CancellationToken cancellationToken)
    {
        ValidateVersion(version);
        var purpose = await repository.FindAsync(id, cancellationToken) ?? throw new PurposeNotFoundException();
        if (purpose.Version != version) throw new PurposeConflictException();
        await repository.DeleteAsync(purpose, clock.GetUtcNow(), cancellationToken);
    }

    private static void ValidateVersion(Guid? version)
    {
        if (version is null || version == Guid.Empty)
            throw new ValidationException([new ValidationFailure("Version", "A non-empty current version is required.")]);
    }
}
using System.ComponentModel.DataAnnotations;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Todo.Application.Security;
using Todo.Domain.Tasks;

namespace Todo.Infrastructure.Persistence;

public sealed class TaskStorageOptions
{
    public bool UseJsonFile { get; set; }
}

public sealed class TaskGrants(TodoDbContext database, UserManager<IdentityUser<Guid>> users,
    ICurrentUser currentUser, IOptions<TaskStorageOptions> options) : ITaskGrants
{
    private string Storage => options.Value.UseJsonFile ? "json" : "sql";
    private IQueryable<TaskShare> Shares => database.TaskShares.Where(share => share.Storage == Storage);

    public Task<bool> CanViewAsync(Guid taskId, CancellationToken cancellationToken) =>
        Shares.AnyAsync(share => share.TaskId == taskId && share.UserId == currentUser.Id, cancellationToken);

    public Task<bool> CanEditAsync(Guid taskId, CancellationToken cancellationToken) =>
        Shares.AnyAsync(share => share.TaskId == taskId && share.UserId == currentUser.Id && share.CanEdit, cancellationToken);

    public async Task<IReadOnlyList<Guid>> SharedIdsAsync(CancellationToken cancellationToken, bool editOnly = false) =>
        await Shares.Where(share => share.UserId == currentUser.Id && (!editOnly || share.CanEdit))
            .Select(share => share.TaskId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<TaskShareResponse>> ListAsync(Guid taskId, CancellationToken cancellationToken) =>
        await (from share in Shares.AsNoTracking()
               join user in database.Users on share.UserId equals user.Id
               where share.TaskId == taskId
               orderby user.Email
               select new TaskShareResponse(user.Id, user.Email!, share.CanEdit)).ToListAsync(cancellationToken);

    public async Task GrantAsync(Guid taskId, ShareTaskRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || request.Email.Length > 254 || !new EmailAddressAttribute().IsValid(request.Email.Trim()))
            throw new FluentValidation.ValidationException([new ValidationFailure("Email", "Enter a valid registered email address.")]);
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null || user.Id == currentUser.Id
            || (await users.GetClaimsAsync(user)).Any(claim => claim.Type == GuestIdentity.ClaimType && claim.Value == "true"))
            throw new FluentValidation.ValidationException([new ValidationFailure("Email", "Choose another registered account.")]);
        var share = await Shares.SingleOrDefaultAsync(item => item.TaskId == taskId && item.UserId == user.Id, cancellationToken);
        if (share is null) database.TaskShares.Add(new TaskShare { TaskId = taskId, UserId = user.Id, Storage = Storage, CanEdit = request.CanEdit });
        else share.CanEdit = request.CanEdit;
        try { await database.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException exception) when (exception.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 })
        { throw new Todo.Application.Tasks.TaskConflictException(); }
    }

    public async Task RevokeAsync(Guid taskId, Guid userId, CancellationToken cancellationToken) =>
        await Shares.Where(share => share.TaskId == taskId && share.UserId == userId).ExecuteDeleteAsync(cancellationToken);

    public async Task ClearAsync(Guid taskId, CancellationToken cancellationToken) =>
        await Shares.Where(share => share.TaskId == taskId).ExecuteDeleteAsync(cancellationToken);
}
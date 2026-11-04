using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Todo.Domain.Tasks;
using Todo.Domain.Purposes;

namespace Todo.Infrastructure.Persistence;

public sealed class TodoDbContext(DbContextOptions<TodoDbContext> options) : IdentityUserContext<IdentityUser<Guid>, Guid>(options)
{
    public DbSet<TodoTask> Tasks => Set<TodoTask>();
    public DbSet<Purpose> Purposes => Set<Purpose>();
    public DbSet<TaskShare> TaskShares => Set<TaskShare>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        var task = modelBuilder.Entity<TodoTask>();
        task.ToTable("Tasks", table =>
        {
            table.HasCheckConstraint("CK_Tasks_Title", "LEN(LTRIM(RTRIM([Title]))) BETWEEN 1 AND 200");
            table.HasCheckConstraint("CK_Tasks_Dates", "[DueDate] IS NULL OR [StartDate] IS NULL OR [DueDate] >= [StartDate]");
            table.HasCheckConstraint("CK_Tasks_Status", "[Status] IN ('Pending', 'InProgress', 'Completed')");
            table.HasCheckConstraint("CK_Tasks_Priority", "[Priority] IN ('Low', 'Normal', 'High', 'Urgent')");
            table.HasCheckConstraint("CK_Tasks_Completion", "([Status] = 'Completed' AND [CompletedAt] IS NOT NULL) OR ([Status] <> 'Completed' AND [CompletedAt] IS NULL)");
        });
        task.HasKey(entity => entity.Id);
        task.HasOne<IdentityUser<Guid>>().WithMany().HasForeignKey(entity => entity.OwnerId).OnDelete(DeleteBehavior.Restrict);
        task.HasIndex(entity => new { entity.OwnerId, entity.CreatedAt });
        task.Property(entity => entity.Title).HasMaxLength(200).IsRequired();
        task.Property(entity => entity.Description).HasMaxLength(4000);
        task.Property(entity => entity.Status).HasConversion<string>().HasMaxLength(20);
        task.Property(entity => entity.Priority).HasConversion<string>().HasMaxLength(20);
        task.Property(entity => entity.Version).IsConcurrencyToken();
        task.HasIndex(entity => new { entity.Status, entity.DueDate });
        task.HasIndex(entity => new { entity.CreatedAt, entity.Id });
        task.HasOne(entity => entity.Purpose).WithMany().HasForeignKey(entity => entity.PurposeId)
            .OnDelete(DeleteBehavior.SetNull);

        var purpose = modelBuilder.Entity<Purpose>();
        purpose.ToTable("Purposes", table => table.HasCheckConstraint("CK_Purposes_Title",
            "LEN(LTRIM(RTRIM([Title]))) BETWEEN 1 AND 200"));
        purpose.HasKey(entity => entity.Id);
        purpose.HasOne<IdentityUser<Guid>>().WithMany().HasForeignKey(entity => entity.OwnerId).OnDelete(DeleteBehavior.Restrict);
        purpose.HasIndex(entity => new { entity.OwnerId, entity.Title });
        purpose.Property(entity => entity.Title).HasMaxLength(200).IsRequired();
        purpose.Property(entity => entity.Description).HasMaxLength(4000);
        purpose.Property(entity => entity.Version).IsConcurrencyToken();
        purpose.HasIndex(entity => new { entity.Title, entity.Id });

        var share = modelBuilder.Entity<TaskShare>();
        share.HasKey(entity => new { entity.Storage, entity.TaskId, entity.UserId });
        share.Property(entity => entity.Storage).HasMaxLength(8);
        share.HasIndex(entity => new { entity.UserId, entity.Storage });
        share.HasOne<IdentityUser<Guid>>().WithMany().HasForeignKey(entity => entity.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
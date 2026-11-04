using FluentValidation;

namespace Todo.Application.Tasks;

public sealed class SaveTaskValidator : AbstractValidator<SaveTaskRequest>
{
    public SaveTaskValidator()
    {
        RuleFor(request => request.Title).NotEmpty().MaximumLength(200);
        RuleFor(request => request.Description).MaximumLength(4000);
        RuleFor(request => request.Status).IsInEnum();
        RuleFor(request => request.Priority).IsInEnum();
        RuleFor(request => request.PurposeId).NotEqual(Guid.Empty).When(request => request.PurposeId.HasValue);
        RuleFor(request => request.DueDate)
            .Must((request, due) => !due.HasValue || !request.StartDate.HasValue || due >= request.StartDate)
            .WithMessage("Due date cannot precede start date.");
    }
}

public sealed class TaskQueryValidator : AbstractValidator<TaskQuery>
{
    public TaskQueryValidator()
    {
        RuleFor(query => query.Search).MaximumLength(200);
        RuleFor(query => query.Page).InclusiveBetween(1, 1_000_000);
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
        RuleFor(query => query.PurposeId).NotEqual(Guid.Empty).When(query => query.PurposeId.HasValue);
        RuleFor(query => query.Uncategorized).Equal(false).When(query => query.PurposeId.HasValue)
            .WithMessage("Choose either a purpose or uncategorized tasks, not both.");
        RuleFor(query => query.SortBy).Must(sort =>
            sort is "createdAt" or "updatedAt" or "title" or "dueDate" or "priority")
            .WithMessage("Sort by createdAt, updatedAt, title, dueDate, or priority.");
        RuleFor(query => query.Status).IsInEnum().When(query => query.Status.HasValue);
        RuleFor(query => query.Priority).IsInEnum().When(query => query.Priority.HasValue);
        RuleFor(query => query.DueTo)
            .Must((query, due) => !due.HasValue || !query.DueFrom.HasValue || due >= query.DueFrom)
            .WithMessage("DueTo cannot precede DueFrom.");
    }
}
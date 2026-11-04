namespace Todo.Application.Security;

public interface ICurrentUser
{
    Guid Id { get; }
}

public sealed class AccessDeniedException : Exception;
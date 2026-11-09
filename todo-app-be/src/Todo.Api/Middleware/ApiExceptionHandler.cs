using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Todo.Application.Tasks;
using Todo.Application.Purposes;
using Todo.Infrastructure.Persistence;
using Todo.Application.Security;
using Microsoft.AspNetCore.Antiforgery;

namespace Todo.Api.Middleware;

public sealed class ApiExceptionHandler(IProblemDetailsService problems, ILogger<ApiExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception,
        CancellationToken cancellationToken)
    {
        ProblemDetails problem = exception switch
        {
            AccessDeniedException => new ProblemDetails { Status = 403, Title = "You do not have permission to change this record." },
            AntiforgeryValidationException => new ProblemDetails { Status = 400, Title = "Your security token expired. Refresh the page and try again." },
            ValidationException validation => new HttpValidationProblemDetails(validation.Errors
                .GroupBy(error => error.PropertyName)
                .ToDictionary(group => group.Key, group => group.Select(error => error.ErrorMessage).Distinct().ToArray()))
                { Status = 400, Title = "Request validation failed." },
            BadHttpRequestException badRequest => new ProblemDetails
                { Status = badRequest.StatusCode, Title = "Invalid HTTP request.", Detail = "Check the request body and parameter types." },
            TaskNotFoundException => new ProblemDetails { Status = 404, Title = "Task not found." },
            PurposeNotFoundException => new ProblemDetails { Status = 404, Title = "Purpose not found." },
            InvalidPurposeException => new HttpValidationProblemDetails(new Dictionary<string, string[]>
                { ["PurposeId"] = ["The selected purpose does not exist. Reload purposes and choose an existing purpose or no purpose."] })
                { Status = 400, Title = "Request validation failed." },
            GuestImportConflictException => new ProblemDetails
                { Status = 409, Title = "Guest import conflicts with existing data.", Detail = "Retry only the original batch without changing its content." },
            TaskConflictException or PurposeConflictException => new ProblemDetails
                { Status = 409, Title = "The record changed or was deleted.", Detail = "Reload the record before retrying with its latest version." },
            FileStorageException => new ProblemDetails
                { Status = 503, Title = "File storage unavailable.", Detail = "The local task file could not be accessed. Check the server logs." },
            SqlException or InvalidOperationException { InnerException: SqlException } => new ProblemDetails
                { Status = 503, Title = "Database unavailable.", Detail = "The data store is temporarily unavailable." },
            _ => new ProblemDetails { Status = 500, Title = "An unexpected error occurred." }
        };

        if (problem.Status >= 500) logger.LogError(exception, "Request failed with status {Status}", problem.Status);
        else logger.LogInformation("Request rejected with status {Status}", problem.Status);
        problem.Instance = context.Request.Path;
        problem.Extensions["traceId"] = context.TraceIdentifier;
        context.Response.StatusCode = problem.Status ?? 500;
        await problems.WriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = problem });
        return true;
    }
}
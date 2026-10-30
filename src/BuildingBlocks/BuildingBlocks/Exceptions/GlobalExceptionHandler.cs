using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Exceptions;

/// <summary>Turns any unhandled exception into an RFC 7807 ProblemDetails response.</summary>
public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var problem = exception switch
        {
            BadRequestException e => new ValidationProblemDetails(e.Errors) { Title = "Bad request", Detail = e.Message, Status = e.StatusCode },
            AppException e => new ProblemDetails { Title = ReasonFor(e.StatusCode), Detail = e.Message, Status = e.StatusCode },
            _ => new ProblemDetails { Title = "Internal server error", Detail = "An unexpected error occurred.", Status = 500 }
        };

        if (problem.Status >= 500)
            logger.LogError(exception, "Unhandled exception on {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        else
            logger.LogWarning("Request failed with {Status}: {Message}", problem.Status, exception.Message);

        httpContext.Response.StatusCode = problem.Status!.Value;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception
        });
    }

    private static string ReasonFor(int status) => status switch
    {
        404 => "Not found",
        409 => "Conflict",
        _ => "Request failed"
    };
}

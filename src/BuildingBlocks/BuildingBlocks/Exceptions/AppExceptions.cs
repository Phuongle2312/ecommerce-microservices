using Microsoft.AspNetCore.Http;

namespace BuildingBlocks.Exceptions;

/// <summary>Base type for expected, client-facing errors. Mapped to ProblemDetails by <see cref="GlobalExceptionHandler"/>.</summary>
public abstract class AppException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public sealed class NotFoundException(string resource, object key)
    : AppException($"{resource} '{key}' was not found.", StatusCodes.Status404NotFound);

public sealed class BadRequestException(string message, IDictionary<string, string[]>? errors = null)
    : AppException(message, StatusCodes.Status400BadRequest)
{
    public IDictionary<string, string[]> Errors { get; } = errors ?? new Dictionary<string, string[]>();
}

public sealed class ConflictException(string message) : AppException(message, StatusCodes.Status409Conflict);

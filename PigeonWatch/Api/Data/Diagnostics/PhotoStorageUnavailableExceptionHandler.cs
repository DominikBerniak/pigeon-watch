using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using PigeonWatch.BusinessObjects;

namespace PigeonWatch.Data.Diagnostics;

public class PhotoStorageUnavailableExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<PhotoStorageUnavailableExceptionHandler> logger) : IExceptionHandler
{
    private const string retryAfterSeconds = "10";

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken = default)
    {
        PhotoStorageUnavailableException? storageException = FindStorageException(exception);

        if (storageException is null)
            return false;

        Endpoint? endpoint = httpContext.Features.Get<IExceptionHandlerFeature>()?.Endpoint ?? httpContext.GetEndpoint();
        string route = (endpoint as RouteEndpoint)?.RoutePattern.RawText ?? "unknown";
        string userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous";
        string traceId = Activity.Current?.TraceId.ToString() ?? httpContext.TraceIdentifier;

        logger.LogError(
            exception,
            "Photo storage unavailable on {Route} for user {UserId} (trace {TraceId}); responding 503 with Retry-After {RetryAfterSeconds} s.",
            route,
            userId,
            traceId,
            retryAfterSeconds);
        httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        httpContext.Response.Headers.RetryAfter = retryAfterSeconds;

        ValidationProblemDetails problemDetails = new(new Dictionary<string, string[]>
        {
            [SightingErrorCodes.PhotoStorageUnavailable] = ["Photo storage is not available. Retry the request shortly or remove the photo."]
        })
        {
            Status = StatusCodes.Status503ServiceUnavailable,
            Title = "Photo storage unavailable"
        };

        await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problemDetails
        });

        return true;
    }

    private static PhotoStorageUnavailableException? FindStorageException(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PhotoStorageUnavailableException storageException)
                return storageException;
        }

        return null;
    }
}

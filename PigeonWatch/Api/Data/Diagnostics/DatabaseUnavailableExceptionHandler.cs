using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Storage;

namespace PigeonWatch.Data.Diagnostics;

public class DatabaseUnavailableExceptionHandler(IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    private const string retryAfterSeconds = "10";

    private static readonly HashSet<int> unavailableErrorNumbers =
    [
        40613, 40197, 40501, 49918, 49919, 49920,
        -2, 53, 258, 10053, 10054, 10060, 233, 64, 121
    ];

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken = default)
    {
        if (!IsDatabaseUnavailable(exception))
            return false;

        httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        httpContext.Response.Headers.RetryAfter = retryAfterSeconds;

        await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "Service warming up",
                Detail = "The database is not available yet. Retry the request shortly."
            }
        });

        return true;
    }

    private static bool IsDatabaseUnavailable(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is RetryLimitExceededException
                || (current is SqlException sqlException && unavailableErrorNumbers.Contains(sqlException.Number)))
                return true;
        }

        return false;
    }
}

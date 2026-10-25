using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace PigeonWatch.WebApi.Controllers;

public abstract class FailureLoggingControllerBase(ILogger logger) : ControllerBase
{
    private const string retryAfterSecondsExtension = "retryAfterSeconds";

    protected UnauthorizedResult UnauthorizedForInvalidIdentity()
    {
        logger.LogWarning("Authenticated request has a missing or malformed user identifier on {Route}", GetRoute());

        return Unauthorized();
    }

    protected UnauthorizedResult UnauthorizedForMissingUser()
    {
        logger.LogWarning("Authenticated user no longer exists on {Route}", GetRoute());

        return Unauthorized();
    }

    protected void LogInvalidInput(string errorCode)
    {
        logger.LogWarning("Request rejected with {ErrorCode} on {Route}", errorCode, GetRoute());
    }

    protected NotFoundResult NotFoundForSighting(Guid sightingId)
    {
        logger.LogInformation("Sighting {SightingId} was not found", sightingId);

        return NotFound();
    }

    protected ActionResult<TResponseModel> GetValidationProblem<TResponseModel>(IEnumerable<(string Code, string Description)> errors, int? retryAfterSeconds = null)
    {
        foreach ((string code, string description) in errors)
            ModelState.AddModelError(code, description);

        ActionResult problem = ValidationProblem(ModelState);

        if (retryAfterSeconds is int seconds && problem is ObjectResult { Value: ProblemDetails details })
        {
            details.Extensions[retryAfterSecondsExtension] = seconds;
            Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
        }

        return problem;
    }

    private string GetRoute()
    {
        string route = (HttpContext.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "unmatched";

        return route;
    }
}

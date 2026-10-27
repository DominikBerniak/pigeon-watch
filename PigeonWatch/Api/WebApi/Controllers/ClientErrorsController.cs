using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using PigeonWatch.WebApi.Models;
using PigeonWatch.WebApi.RateLimiting;

namespace PigeonWatch.WebApi.Controllers;

[ApiController]
[Route("client-errors")]
public class ClientErrorsController(ILogger<ClientErrorsController> logger) : FailureLoggingControllerBase(logger)
{
    private static readonly Dictionary<string, LogLevel> levelsByKind = new()
    {
        ["uncaught"] = LogLevel.Warning,
        ["bootstrap"] = LogLevel.Warning,
        ["navigation"] = LogLevel.Warning,
        ["api-failure"] = LogLevel.Warning,
        ["session-expired"] = LogLevel.Warning,
        ["warmup-exhausted"] = LogLevel.Warning,
        ["fallback"] = LogLevel.Information
    };

    [HttpPost]
    [AllowAnonymous]
    [RequestSizeLimit(2048)]
    [EnableRateLimiting(RateLimitPolicyNames.ClientErrors)]
    public Task<ActionResult<ClientErrorReceiptModel>> Report(ClientErrorReportModel request, CancellationToken cancellationToken)
    {
        LogLevel level = levelsByKind.GetValueOrDefault(request.Kind, LogLevel.Warning);

        logger.Log(
            level,
            "Client reported {Kind} at {Context} with status {HttpStatus} and error {ErrorName} on {Route} in session {SessionId} (client trace {ClientTraceId})",
            request.Kind,
            request.Context,
            request.HttpStatus,
            request.ErrorName,
            request.Route,
            request.SessionId,
            request.TraceId);

        return Task.FromResult<ActionResult<ClientErrorReceiptModel>>(Accepted());
    }
}

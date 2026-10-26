using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace PigeonWatch.Data.Auditing;

public class HttpContextCurrentUserProvider(
    IHttpContextAccessor httpContextAccessor,
    ILogger<HttpContextCurrentUserProvider> logger) : ICurrentUserProvider
{
    public const string SystemUserName = "SYSTEM";

    public string GetCurrentUserName()
    {
        ClaimsPrincipal? user = httpContextAccessor.HttpContext?.User;

        if (user?.Identity?.IsAuthenticated != true)
            return SystemUserName;

        string? userId = user.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrEmpty(userId))
        {
            logger.LogWarning("Authenticated principal has no NameIdentifier claim, recording the audit user as {AuditUser}", SystemUserName);

            return SystemUserName;
        }

        return userId;
    }
}

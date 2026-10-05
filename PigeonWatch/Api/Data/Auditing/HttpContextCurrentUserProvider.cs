using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace PigeonWatch.Data.Auditing;

public class HttpContextCurrentUserProvider(IHttpContextAccessor httpContextAccessor) : ICurrentUserProvider
{
    public const string SystemUserName = "SYSTEM";

    public string GetCurrentUserName()
    {
        ClaimsPrincipal? user = httpContextAccessor.HttpContext?.User;

        if (user?.Identity?.IsAuthenticated != true)
            return SystemUserName;

        string? userId = user.FindFirstValue(ClaimTypes.NameIdentifier);

        return string.IsNullOrEmpty(userId) ? SystemUserName : userId;
    }
}

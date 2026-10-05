using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using PigeonWatch.Data.Auditing;

namespace PigeonWatch.UnitTests;

public class HttpContextCurrentUserProviderTests
{
    [Fact]
    public void Authenticated_user_is_recorded_by_user_id()
    {
        Guid userId = Guid.NewGuid();
        ClaimsIdentity identity = new(
            [new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim(ClaimTypes.Name, "user@example.com")],
            "Bearer");
        HttpContextAccessor accessor = new() { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) } };

        string userName = new HttpContextCurrentUserProvider(accessor).GetCurrentUserName();

        Assert.Equal(userId.ToString(), userName);
    }

    [Fact]
    public void Anonymous_request_is_recorded_as_system()
    {
        HttpContextAccessor accessor = new() { HttpContext = new DefaultHttpContext() };

        string userName = new HttpContextCurrentUserProvider(accessor).GetCurrentUserName();

        Assert.Equal(HttpContextCurrentUserProvider.SystemUserName, userName);
    }

    [Fact]
    public void Missing_http_context_is_recorded_as_system()
    {
        string userName = new HttpContextCurrentUserProvider(new HttpContextAccessor()).GetCurrentUserName();

        Assert.Equal(HttpContextCurrentUserProvider.SystemUserName, userName);
    }
}

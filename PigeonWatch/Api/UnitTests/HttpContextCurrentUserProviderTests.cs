using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.Core;
using PigeonWatch.Data.Auditing;

namespace PigeonWatch.UnitTests;

public class HttpContextCurrentUserProviderTests
{
    private readonly ILogger<HttpContextCurrentUserProvider> logger = Substitute.For<ILogger<HttpContextCurrentUserProvider>>();

    [Fact]
    public void Authenticated_user_is_recorded_by_user_id()
    {
        Guid userId = Guid.NewGuid();
        ClaimsIdentity identity = new(
            [new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim(ClaimTypes.Name, "user@example.com")],
            "Bearer");
        HttpContextAccessor accessor = new() { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) } };

        string userName = CreateProvider(accessor).GetCurrentUserName();

        Assert.Equal(userId.ToString(), userName);
    }

    [Fact]
    public void Anonymous_request_is_recorded_as_system()
    {
        HttpContextAccessor accessor = new() { HttpContext = new DefaultHttpContext() };

        string userName = CreateProvider(accessor).GetCurrentUserName();

        Assert.Equal(HttpContextCurrentUserProvider.SystemUserName, userName);
    }

    [Fact]
    public void Missing_http_context_is_recorded_as_system()
    {
        string userName = CreateProvider(new HttpContextAccessor()).GetCurrentUserName();

        Assert.Equal(HttpContextCurrentUserProvider.SystemUserName, userName);
    }

    [Fact]
    public void Authenticated_principal_without_a_name_identifier_logs_one_warning_and_is_recorded_as_system()
    {
        ClaimsIdentity identity = new([new Claim(ClaimTypes.Name, "user@example.com")], "Bearer");
        HttpContextAccessor accessor = new() { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) } };

        string userName = CreateProvider(accessor).GetCurrentUserName();

        Assert.Equal(HttpContextCurrentUserProvider.SystemUserName, userName);
        ICall logCall = Assert.Single(LogCalls());
        Assert.Equal(LogLevel.Warning, logCall.GetArguments()[0]);
    }

    [Fact]
    public void Anonymous_request_logs_nothing()
    {
        HttpContextAccessor accessor = new() { HttpContext = new DefaultHttpContext() };

        CreateProvider(accessor).GetCurrentUserName();

        Assert.Empty(LogCalls());
    }

    [Fact]
    public void Authenticated_user_with_a_name_identifier_logs_nothing()
    {
        ClaimsIdentity identity = new([new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], "Bearer");
        HttpContextAccessor accessor = new() { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) } };

        CreateProvider(accessor).GetCurrentUserName();

        Assert.Empty(LogCalls());
    }

    private HttpContextCurrentUserProvider CreateProvider(IHttpContextAccessor accessor)
    {
        return new(accessor, logger);
    }

    private IEnumerable<ICall> LogCalls()
    {
        return logger.ReceivedCalls().Where(call => call.GetMethodInfo().Name == nameof(ILogger.Log));
    }
}

using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using PigeonWatch.Data.Identity;
using PigeonWatch.WebApi.RateLimiting;

namespace PigeonWatch.DependencyInjection;

public static class PigeonWatchEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapPigeonWatch(this IEndpointRouteBuilder endpoints)
    {
        string[] exposedIdentityRoutes = ["auth/login", "auth/refresh"];
        string[] cookieFlags = ["useCookies", "useSessionCookies"];

        RouteGroupBuilder authGroup = endpoints
            .MapGroup("auth")
            .RequireRateLimiting(RateLimitPolicyNames.Auth);

        authGroup.AddEndpointFilter(async (invocationContext, next) =>
        {
            object? result = await next(invocationContext);
            object? inner = result is INestedHttpResult nested ? nested.Result : result;

            HttpContext httpContext = invocationContext.HttpContext;
            string route = string.Join(
                '/',
                ((httpContext.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? string.Empty)
                    .Split('/', StringSplitOptions.RemoveEmptyEntries));

            if (!exposedIdentityRoutes.Contains(route, StringComparer.OrdinalIgnoreCase))
                return result;

            string outcome;
            LogLevel level;

            if (inner is ProblemHttpResult problem)
            {
                string? detail = problem.ProblemDetails.Detail;
                int statusCode = problem.ProblemDetails.Status ?? problem.StatusCode;

                if (string.Equals(detail, "LockedOut", StringComparison.Ordinal))
                    outcome = "LockedOut";
                else if (string.Equals(detail, "NotAllowed", StringComparison.Ordinal))
                    outcome = "NotAllowed";
                else if (statusCode == StatusCodes.Status403Forbidden)
                    outcome = "NotAllowed";
                else
                    outcome = "Failed";

                level = LogLevel.Warning;
            }
            else if (inner is ChallengeHttpResult or ForbidHttpResult or UnauthorizedHttpResult)
            {
                outcome = "Rejected";
                level = LogLevel.Warning;
            }
            else if (inner is IStatusCodeHttpResult { StatusCode: >= StatusCodes.Status400BadRequest })
            {
                outcome = "Failed";
                level = LogLevel.Warning;
            }
            else
            {
                outcome = "Succeeded";
                level = LogLevel.Information;
            }

            ILogger logger = httpContext.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("PigeonWatch.AuthEvents");

            if (Guid.TryParse(httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), out Guid userId))
                logger.Log(level, "Authentication {Outcome} on {Route} for {UserId}", outcome, route, userId);
            else
                logger.Log(level, "Authentication {Outcome} on {Route}", outcome, route);

            return result;
        });

        authGroup
            .MapIdentityApi<ApplicationUser>()
            .Finally(endpointBuilder =>
            {
                string route = string.Join(
                    '/',
                    ((endpointBuilder as RouteEndpointBuilder)?.RoutePattern.RawText ?? string.Empty)
                        .Split('/', StringSplitOptions.RemoveEmptyEntries));
                bool isPost = endpointBuilder.Metadata
                    .OfType<IHttpMethodMetadata>()
                    .SelectMany(metadata => metadata.HttpMethods)
                    .Contains(HttpMethods.Post, StringComparer.OrdinalIgnoreCase);

                if (!isPost || !exposedIdentityRoutes.Contains(route, StringComparer.OrdinalIgnoreCase))
                {
                    endpointBuilder.Metadata.Add(new ExcludeFromDescriptionAttribute());
                    endpointBuilder.RequestDelegate = context =>
                    {
                        context.RequestServices
                            .GetRequiredService<ILoggerFactory>()
                            .CreateLogger("PigeonWatch.AuthEvents")
                            .LogInformation(
                                "Stubbed identity route {Route} hit with {Method}",
                                route,
                                context.Request.Method);
                        context.Response.StatusCode = StatusCodes.Status404NotFound;

                        return Task.CompletedTask;
                    };

                    return;
                }

                RequestDelegate? identityHandler = endpointBuilder.RequestDelegate;
                endpointBuilder.RequestDelegate = context =>
                {
                    bool requestsCookies = cookieFlags.Any(flag =>
                        context.Request.Query.TryGetValue(flag, out StringValues values)
                        && !values.All(value => string.Equals(value, bool.FalseString, StringComparison.OrdinalIgnoreCase)));

                    if (requestsCookies)
                    {
                        context.RequestServices
                            .GetRequiredService<ILoggerFactory>()
                            .CreateLogger("PigeonWatch.AuthEvents")
                            .LogInformation(
                                "Cookie sign-in rejected on {Route} with {Method}",
                                route,
                                context.Request.Method);

                        return Results.Problem(
                            detail: "Cookie sign-in is not supported; use bearer tokens.",
                            statusCode: StatusCodes.Status400BadRequest).ExecuteAsync(context);
                    }

                    return identityHandler is null ? Task.CompletedTask : identityHandler(context);
                };
            });

        return endpoints;
    }
}

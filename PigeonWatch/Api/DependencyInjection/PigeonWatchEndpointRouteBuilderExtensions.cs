using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
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

        endpoints
            .MapGroup("auth")
            .RequireRateLimiting(RateLimitPolicyNames.Auth)
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

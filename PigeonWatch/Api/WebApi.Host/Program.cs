using System.Collections;
using System.Diagnostics;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.RateLimiting;
using PigeonWatch.DependencyInjection;
using PigeonWatch.WebApi.RateLimiting;

namespace PigeonWatch.WebApi.Host
{
    public class Program
    {
        public static void Main(string[] args)
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

            builder.Services.AddControllers();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen(options =>
            {
                options.SwaggerDoc("v1", new Microsoft.OpenApi.OpenApiInfo
                {
                    Title = "PigeonWatch API",
                    Version = "v1"
                });
            });

            builder.Services.AddPigeonWatch(builder.Configuration);

            builder.Services.AddCors(options =>
            {
                options.AddPolicy("Frontend", policy =>
                {
                    policy.WithOrigins("http://localhost:4200")
                        .AllowAnyHeader()
                        .AllowAnyMethod();
                });
            });

            builder.Services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.OnRejected = (context, token) =>
                {
                    Endpoint? endpoint = context.HttpContext.GetEndpoint();
                    string policyName = endpoint?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName ?? "unknown";
                    string route = (endpoint as RouteEndpoint)?.RoutePattern.RawText ?? "unmatched";
                    bool remoteIpMissing = context.HttpContext.Connection.RemoteIpAddress is null;

                    context.HttpContext.RequestServices
                        .GetRequiredService<ILoggerFactory>()
                        .CreateLogger("PigeonWatch.RateLimiting")
                        .LogWarning(
                            "Rate limit rejected {Route} under policy {PolicyName} (remote IP missing: {RemoteIpMissing})",
                            route,
                            policyName,
                            remoteIpMissing);

                    return ValueTask.CompletedTask;
                };
                options.AddPolicy(RateLimitPolicyNames.Auth, httpContext =>
                {
                    if (httpContext.Request.Path.Value?.EndsWith("/auth/refresh", StringComparison.OrdinalIgnoreCase) == true)
                        return RateLimitPartition.GetNoLimiter(string.Empty);

                    return RateLimitPartition.GetFixedWindowLimiter(
                        httpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 10,
                            Window = TimeSpan.FromMinutes(1),
                            QueueLimit = 0
                        });
                });
            });

            WebApplication app = builder.Build();

            app.Use(async (context, next) =>
            {
                long startedAt = Stopwatch.GetTimestamp();
                RequestLogScope scope = new(Activity.Current?.Id ?? context.TraceIdentifier);
                context.Features.Set(scope);

                using IDisposable? loggerScope = app.Logger.BeginScope(scope);

                try
                {
                    await next(context);
                }
                finally
                {
                    Endpoint? endpoint = context.GetEndpoint() ?? context.Features.Get<IExceptionHandlerFeature>()?.Endpoint;
                    string route = (endpoint as RouteEndpoint)?.RoutePattern.RawText ?? "unmatched";
                    double elapsedMs = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;

                    app.Logger.LogInformation(
                        "HTTP {Method} {Route} responded {StatusCode} in {ElapsedMs} ms",
                        context.Request.Method,
                        route,
                        context.Response.StatusCode,
                        elapsedMs);
                }
            });

            app.UseExceptionHandler();

            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }
            else
                app.UseHttpsRedirection();

            app.UseRouting();

            app.UseCors("Frontend");

            app.UseAuthentication();

            app.Use(async (context, next) =>
            {
                RequestLogScope? scope = context.Features.Get<RequestLogScope>();

                if (scope is not null && Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out Guid userId))
                    scope.UserId = userId;

                await next(context);
            });

            app.UseRateLimiter();

            app.UseAuthorization();

            app.MapControllers();

            app.MapPigeonWatch();

            app.Run();
        }

        private sealed class RequestLogScope(string traceId) : IReadOnlyList<KeyValuePair<string, object?>>
        {
            public Guid? UserId { get; set; }

            public int Count => UserId is null ? 1 : 2;

            public KeyValuePair<string, object?> this[int index]
            {
                get
                {
                    if (index == 0)
                        return new KeyValuePair<string, object?>("TraceId", traceId);

                    if (index == 1 && UserId is not null)
                        return new KeyValuePair<string, object?>("UserId", UserId);

                    throw new ArgumentOutOfRangeException(nameof(index));
                }
            }

            public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
            {
                for (int index = 0; index < Count; index++)
                    yield return this[index];
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }

            public override string ToString()
            {
                return UserId is null ? $"TraceId:{traceId}" : $"TraceId:{traceId} UserId:{UserId}";
            }
        }
    }
}

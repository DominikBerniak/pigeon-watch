using System.Threading.RateLimiting;
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

            app.UseRateLimiter();

            app.UseAuthorization();

            app.MapControllers();

            app.MapPigeonWatch();

            app.Run();
        }
    }
}

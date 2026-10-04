using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.EntityFrameworkCore.Migrations;
using PigeonWatch.Data;
using PigeonWatch.Data.Auditing;
using PigeonWatch.Data.Conventions;
using PigeonWatch.Data.Mappers;
using PigeonWatch.Data.Repositories;

namespace PigeonWatch.DependencyInjection;

public static class DataServiceCollectionExtensions
{
    public static IServiceCollection AddData(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserProvider, HttpContextCurrentUserProvider>();
        services.AddScoped<ISaveChangesInterceptor, AuditSaveChangesInterceptor>();

        services.AddDbContext<PigeonWatchDbContext>((serviceProvider, options) =>
            options
                .AddInterceptors(serviceProvider.GetServices<ISaveChangesInterceptor>())
                .UseSqlServer(
                    configuration.GetConnectionString("Default"),
                    sql => sql
                        .EnableRetryOnFailure()
                        .MigrationsHistoryTable(UpperSnakeCaseMigrationsHistory.HistoryTableName))
                .ReplaceService<IHistoryRepository, UpperSnakeCaseMigrationsHistory>());

        services.AddScoped<ISmokeCheckMapper, SmokeCheckMapper>();
        services.AddScoped<ISmokeCheckRepository, SmokeCheckRepository>();
        services.AddScoped<IUserAccountMapper, UserAccountMapper>();
        services.AddScoped<IAccountRepository, AccountRepository>();

        return services;
    }
}

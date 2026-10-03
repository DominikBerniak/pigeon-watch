using Microsoft.Extensions.DependencyInjection;
using PigeonWatch.BusinessLogic.Services;

namespace PigeonWatch.DependencyInjection;

public static class BusinessLogicServiceCollectionExtensions
{
    public static IServiceCollection AddBusinessLogic(this IServiceCollection services)
    {
        services.AddScoped<IDatabaseHealthService, DatabaseHealthService>();

        return services;
    }
}

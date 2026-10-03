using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PigeonWatch.DependencyInjection;

public static class PigeonWatchServiceCollectionExtensions
{
    public static IServiceCollection AddPigeonWatch(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddData(configuration);
        services.AddBusinessLogic();
        services.AddWebApi();

        return services;
    }
}

using Microsoft.Extensions.DependencyInjection;
using PigeonWatch.WebApi.ViewModelCreators;

namespace PigeonWatch.DependencyInjection;

public static class WebApiServiceCollectionExtensions
{
    public static IServiceCollection AddWebApi(this IServiceCollection services)
    {
        services.AddScoped<IDatabaseHealthViewModelCreator, DatabaseHealthViewModelCreator>();

        return services;
    }
}

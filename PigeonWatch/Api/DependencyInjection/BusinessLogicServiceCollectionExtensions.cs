using Microsoft.Extensions.DependencyInjection;
using PigeonWatch.BusinessLogic.Services;

namespace PigeonWatch.DependencyInjection;

public static class BusinessLogicServiceCollectionExtensions
{
    public static IServiceCollection AddBusinessLogic(this IServiceCollection services)
    {
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<IClientConfigurationService, ClientConfigurationService>();
        services.AddScoped<IGeneralConfigurationService, GeneralConfigurationService>();
        services.AddScoped<IUiResourceService, UiResourceService>();

        return services;
    }
}

using Microsoft.Extensions.DependencyInjection;
using PigeonWatch.WebApi.Mappers;
using PigeonWatch.WebApi.ViewModelCreators;

namespace PigeonWatch.DependencyInjection;

public static class WebApiServiceCollectionExtensions
{
    public static IServiceCollection AddWebApi(this IServiceCollection services)
    {
        services.AddScoped<IRegisterRequestMapper, RegisterRequestMapper>();
        services.AddScoped<IRegisteredAccountViewModelCreator, RegisteredAccountViewModelCreator>();
        services.AddScoped<IUpdatedProfileViewModelCreator, UpdatedProfileViewModelCreator>();
        services.AddScoped<IPasswordChangedViewModelCreator, PasswordChangedViewModelCreator>();
        services.AddScoped<IClientConfigurationViewModelCreator, ClientConfigurationViewModelCreator>();
        services.AddScoped<IGeneralConfigurationViewModelCreator, GeneralConfigurationViewModelCreator>();
        services.AddScoped<IUiResourcesViewModelCreator, UiResourcesViewModelCreator>();

        return services;
    }
}

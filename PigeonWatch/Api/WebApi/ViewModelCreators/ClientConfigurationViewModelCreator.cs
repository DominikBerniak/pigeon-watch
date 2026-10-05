using PigeonWatch.BusinessObjects;
using PigeonWatch.WebApi.Models;

namespace PigeonWatch.WebApi.ViewModelCreators;

public class ClientConfigurationViewModelCreator : IClientConfigurationViewModelCreator
{
    public ClientConfigurationModel Create(ClientConfiguration configuration)
    {
        return new()
        {
            PasswordRules = new PasswordRulesModel
            {
                MinLength = configuration.PasswordMinLength,
                RequireDigit = configuration.PasswordRequiresDigit,
                RequireLowercase = configuration.PasswordRequiresLowercase,
                RequireUppercase = configuration.PasswordRequiresUppercase,
                RequireNonAlphanumeric = configuration.PasswordRequiresNonAlphanumeric
            },
            DisplayNameRules = new DisplayNameRulesModel
            {
                MinLength = configuration.DisplayNameMinLength,
                MaxLength = configuration.DisplayNameMaxLength
            }
        };
    }
}

using PigeonWatch.BusinessObjects;
using PigeonWatch.WebApi.Models;

namespace PigeonWatch.WebApi.ViewModelCreators;

public class GeneralConfigurationViewModelCreator : IGeneralConfigurationViewModelCreator
{
    public GeneralConfigurationModel Create(CurrentUser currentUser, IReadOnlyList<string> roles) =>
        new()
        {
            CurrentUser = new CurrentUserModel
            {
                Id = currentUser.Id,
                Email = currentUser.Email,
                DisplayName = currentUser.DisplayName,
                Roles = roles
            }
        };
}

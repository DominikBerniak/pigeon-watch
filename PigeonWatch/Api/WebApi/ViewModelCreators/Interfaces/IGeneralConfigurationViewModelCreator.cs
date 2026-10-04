using PigeonWatch.BusinessObjects;
using PigeonWatch.WebApi.Models;

namespace PigeonWatch.WebApi.ViewModelCreators;

public interface IGeneralConfigurationViewModelCreator
{
    GeneralConfigurationModel Create(CurrentUser currentUser, IReadOnlyList<string> roles);
}

using PigeonWatch.BusinessObjects;
using PigeonWatch.WebApi.Models;

namespace PigeonWatch.WebApi.ViewModelCreators;

public interface IUpdatedProfileViewModelCreator
{
    UpdatedProfileModel Create(UpdatedProfile profile);
}

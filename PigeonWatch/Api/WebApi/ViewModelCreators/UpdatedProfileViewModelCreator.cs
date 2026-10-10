using PigeonWatch.BusinessObjects;
using PigeonWatch.WebApi.Models;

namespace PigeonWatch.WebApi.ViewModelCreators;

public class UpdatedProfileViewModelCreator : IUpdatedProfileViewModelCreator
{
    public UpdatedProfileModel Create(UpdatedProfile profile)
    {
        return new()
        {
            Email = profile.Email,
            DisplayName = profile.DisplayName
        };
    }
}

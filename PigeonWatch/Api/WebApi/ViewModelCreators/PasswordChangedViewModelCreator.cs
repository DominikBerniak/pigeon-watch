using PigeonWatch.BusinessObjects;
using PigeonWatch.WebApi.Models;

namespace PigeonWatch.WebApi.ViewModelCreators;

public class PasswordChangedViewModelCreator : IPasswordChangedViewModelCreator
{
    public PasswordChangedModel Create(PasswordChangeResult result)
    {
        return new()
        {
            Changed = result.Succeeded
        };
    }
}

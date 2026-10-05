using PigeonWatch.BusinessObjects;
using PigeonWatch.WebApi.Models;

namespace PigeonWatch.WebApi.ViewModelCreators;

public class RegisteredAccountViewModelCreator : IRegisteredAccountViewModelCreator
{
    public RegisteredAccountModel Create(RegisteredAccount account)
    {
        return new()
        {
            Email = account.Email,
            DisplayName = account.DisplayName
        };
    }
}

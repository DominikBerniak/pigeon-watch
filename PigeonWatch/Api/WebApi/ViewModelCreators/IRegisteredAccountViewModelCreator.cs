using PigeonWatch.BusinessObjects;
using PigeonWatch.WebApi.Models;

namespace PigeonWatch.WebApi.ViewModelCreators;

public interface IRegisteredAccountViewModelCreator
{
    RegisteredAccountModel Create(RegisteredAccount account);
}

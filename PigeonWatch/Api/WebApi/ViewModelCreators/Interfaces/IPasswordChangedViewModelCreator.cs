using PigeonWatch.BusinessObjects;
using PigeonWatch.WebApi.Models;

namespace PigeonWatch.WebApi.ViewModelCreators;

public interface IPasswordChangedViewModelCreator
{
    PasswordChangedModel Create(PasswordChangeResult result);
}

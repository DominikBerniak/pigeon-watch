using PigeonWatch.BusinessObjects;
using PigeonWatch.WebApi.Models;

namespace PigeonWatch.WebApi.ViewModelCreators;

public interface IClientConfigurationViewModelCreator
{
    ClientConfigurationModel Create(ClientConfiguration configuration);
}

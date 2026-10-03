using PigeonWatch.BusinessObjects;
using PigeonWatch.WebApi.Models;

namespace PigeonWatch.WebApi.ViewModelCreators;

public interface IDatabaseHealthViewModelCreator
{
    DatabaseHealthModel Create(DatabaseHealthResult result);
}

using PigeonWatch.BusinessObjects;
using PigeonWatch.WebApi.Models;

namespace PigeonWatch.WebApi.ViewModelCreators;

public interface IUiResourcesViewModelCreator
{
    UiResourcesModel Create(UiLabelSet labelSet);
}

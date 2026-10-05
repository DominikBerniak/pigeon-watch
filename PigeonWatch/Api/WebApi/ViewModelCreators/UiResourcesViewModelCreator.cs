using PigeonWatch.BusinessObjects;
using PigeonWatch.WebApi.Models;

namespace PigeonWatch.WebApi.ViewModelCreators;

public class UiResourcesViewModelCreator : IUiResourcesViewModelCreator
{
    public UiResourcesModel Create(UiLabelSet labelSet)
    {
        return new()
        {
            Culture = labelSet.Culture,
            Labels = labelSet.Labels
        };
    }
}

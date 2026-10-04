using PigeonWatch.BusinessObjects;

namespace PigeonWatch.BusinessLogic.Services;

public interface IUiResourceService
{
    Task<UiLabelSet> GetAsync(string culture, CancellationToken cancellationToken = default);
}

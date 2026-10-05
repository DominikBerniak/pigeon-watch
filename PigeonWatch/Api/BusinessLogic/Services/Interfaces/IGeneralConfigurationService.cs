using PigeonWatch.BusinessObjects;

namespace PigeonWatch.BusinessLogic.Services;

public interface IGeneralConfigurationService
{
    Task<CurrentUser?> GetAsync(Guid userId, CancellationToken cancellationToken = default);
}

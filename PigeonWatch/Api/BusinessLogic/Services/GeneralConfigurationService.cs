using PigeonWatch.BusinessObjects;
using PigeonWatch.Data.Repositories;

namespace PigeonWatch.BusinessLogic.Services;

public class GeneralConfigurationService(IAccountRepository accountRepository) : IGeneralConfigurationService
{
    public Task<CurrentUser?> GetAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return accountRepository.GetCurrentUserAsync(userId, cancellationToken);
    }
}

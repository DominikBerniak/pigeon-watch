using PigeonWatch.BusinessObjects;

namespace PigeonWatch.BusinessLogic.Services;

public interface IAccountService
{
    Task<AccountCreationResult> RegisterAsync(NewAccount account, CancellationToken cancellationToken = default);
}

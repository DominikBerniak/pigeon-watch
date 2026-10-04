using PigeonWatch.BusinessObjects;

namespace PigeonWatch.Data.Repositories;

public interface IAccountRepository
{
    Task<AccountCreationResult> CreateAsync(NewAccount account, CancellationToken cancellationToken = default);
}

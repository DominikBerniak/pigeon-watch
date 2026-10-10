using PigeonWatch.BusinessObjects;

namespace PigeonWatch.Data.Repositories;

public interface IAccountRepository
{
    Task<AccountCreationResult> CreateAsync(NewAccount account, CancellationToken cancellationToken = default);
    Task<CurrentUser?> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<ProfileUpdateResult> UpdateDisplayNameAsync(Guid userId, string displayName, CancellationToken cancellationToken = default);
}

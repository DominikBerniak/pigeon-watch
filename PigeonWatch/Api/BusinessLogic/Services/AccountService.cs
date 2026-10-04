using PigeonWatch.BusinessObjects;
using PigeonWatch.Data.Repositories;

namespace PigeonWatch.BusinessLogic.Services;

public class AccountService(IAccountRepository accountRepository) : IAccountService
{
    public async Task<AccountCreationResult> RegisterAsync(NewAccount account, CancellationToken cancellationToken = default)
    {
        NewAccount trimmed = account with
        {
            Email = account.Email.Trim(),
            DisplayName = account.DisplayName.Trim()
        };

        List<AccountError> displayNameErrors = ValidateDisplayName(trimmed.DisplayName);
        if (displayNameErrors.Count > 0)
        {
            return AccountCreationResult.Failure(displayNameErrors);
        }

        return await accountRepository.CreateAsync(trimmed, cancellationToken);
    }

    private static List<AccountError> ValidateDisplayName(string displayName)
    {
        List<AccountError> errors = [];

        if (displayName.Length < AccountRules.DisplayNameMinLength || displayName.Length > AccountRules.DisplayNameMaxLength)
        {
            errors.Add(new AccountError(
                AccountErrorCodes.DisplayNameLength,
                $"Display name must be between {AccountRules.DisplayNameMinLength} and {AccountRules.DisplayNameMaxLength} characters."));
        }

        if (displayName.Contains(AccountRules.DisplayNameForbiddenCharacter))
        {
            errors.Add(new AccountError(
                AccountErrorCodes.DisplayNameInvalidCharacter,
                $"Display name must not contain '{AccountRules.DisplayNameForbiddenCharacter}'."));
        }

        return errors;
    }
}

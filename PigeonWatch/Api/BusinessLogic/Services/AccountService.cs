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

        List<AccountError> errors = [.. ValidateEmail(trimmed.Email), .. ValidateDisplayName(trimmed.DisplayName)];

        if (errors.Count > 0)
            return AccountCreationResult.Failure(errors);

        return await accountRepository.CreateAsync(trimmed, cancellationToken);
    }

    public async Task<ProfileUpdateResult> UpdateDisplayNameAsync(Guid userId, string displayName, CancellationToken cancellationToken = default)
    {
        string trimmed = displayName.Trim();

        List<AccountError> errors = ValidateDisplayName(trimmed);

        if (errors.Count > 0)
            return ProfileUpdateResult.Failure(errors);

        return await accountRepository.UpdateDisplayNameAsync(userId, trimmed, cancellationToken);
    }

    private static List<AccountError> ValidateEmail(string email)
    {
        List<AccountError> errors = [];

        if (email.Length > AccountRules.EmailMaxLength)
            errors.Add(new AccountError(AccountErrorCodes.InvalidEmail, $"Email must be at most {AccountRules.EmailMaxLength} characters."));

        return errors;
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

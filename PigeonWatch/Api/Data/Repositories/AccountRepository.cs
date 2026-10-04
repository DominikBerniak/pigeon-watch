using Microsoft.AspNetCore.Identity;
using PigeonWatch.BusinessObjects;
using PigeonWatch.Data.Identity;

namespace PigeonWatch.Data.Repositories;

public class AccountRepository(UserManager<ApplicationUser> userManager) : IAccountRepository
{
    public async Task<AccountCreationResult> CreateAsync(NewAccount account, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ApplicationUser user = new()
        {
            Email = account.Email,
            UserName = account.Email,
            DisplayName = account.DisplayName
        };

        IdentityResult result = await userManager.CreateAsync(user, account.Password);

        if (result.Succeeded)
            return AccountCreationResult.Success(new RegisteredAccount(user.Email, user.DisplayName));

        return AccountCreationResult.Failure(ToAccountErrors(result.Errors));
    }

    private static List<AccountError> ToAccountErrors(IEnumerable<IdentityError> identityErrors)
    {
        List<IdentityError> errors = identityErrors.ToList();
        HashSet<string> codes = errors.Select(error => error.Code).ToHashSet(StringComparer.Ordinal);
        List<AccountError> accountErrors = [];

        foreach (IdentityError error in errors)
        {
            string code = error.Code switch
            {
                AccountErrorCodes.DuplicateUserName => AccountErrorCodes.DuplicateEmail,
                AccountErrorCodes.InvalidUserName => AccountErrorCodes.InvalidEmail,
                _ => error.Code
            };

            bool collapsesIntoExistingEmailError = code != error.Code && codes.Contains(code);
            bool alreadyAdded = accountErrors.Any(accountError => accountError.Code == code);

            if (!collapsesIntoExistingEmailError && !alreadyAdded)
                accountErrors.Add(new AccountError(code, error.Description));
        }

        return accountErrors;
    }
}

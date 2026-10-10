using Microsoft.AspNetCore.Identity;
using PigeonWatch.BusinessObjects;
using PigeonWatch.Data.Identity;
using PigeonWatch.Data.Mappers;

namespace PigeonWatch.Data.Repositories;

public class AccountRepository(UserManager<ApplicationUser> userManager, IUserAccountMapper userAccountMapper) : IAccountRepository
{
    private const string registrationFailedDescription = "The account could not be created.";
    private const string userNotFoundDescription = "The user could not be found.";

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
            return AccountCreationResult.Success(userAccountMapper.ToRegisteredAccount(user));

        return AccountCreationResult.Failure(ToAccountErrors(result.Errors));
    }

    public async Task<CurrentUser?> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ApplicationUser? user = await userManager.FindByIdAsync(userId.ToString());

        return user is null ? null : userAccountMapper.ToCurrentUser(user);
    }

    public async Task<ProfileUpdateResult> UpdateDisplayNameAsync(Guid userId, string displayName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ApplicationUser? user = await userManager.FindByIdAsync(userId.ToString());

        if (user is null)
            return ProfileUpdateResult.Failure([new AccountError(AccountErrorCodes.UserNotFound, userNotFoundDescription)]);

        user.DisplayName = displayName;

        IdentityResult result = await userManager.UpdateAsync(user);

        if (result.Succeeded)
            return ProfileUpdateResult.Success(userAccountMapper.ToUpdatedProfile(user));

        return ProfileUpdateResult.Failure(ToAccountErrors(result.Errors));
    }

    public async Task<PasswordChangeResult> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ApplicationUser? user = await userManager.FindByIdAsync(userId.ToString());

        if (user is null)
            return PasswordChangeResult.Failure([new AccountError(AccountErrorCodes.UserNotFound, userNotFoundDescription)]);

        IdentityResult result = await userManager.ChangePasswordAsync(user, currentPassword, newPassword);

        if (result.Succeeded)
            return PasswordChangeResult.Success();

        return PasswordChangeResult.Failure(ToAccountErrors(result.Errors));
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
                AccountErrorCodes.DuplicateEmail or AccountErrorCodes.DuplicateUserName => AccountErrorCodes.RegistrationFailed,
                AccountErrorCodes.InvalidUserName => AccountErrorCodes.InvalidEmail,
                _ => error.Code
            };
            string description = code == AccountErrorCodes.RegistrationFailed ? registrationFailedDescription : error.Description;

            bool collapsesIntoExistingEmailError = code != error.Code && codes.Contains(code);
            bool alreadyAdded = accountErrors.Any(accountError => accountError.Code == code);

            if (!collapsesIntoExistingEmailError && !alreadyAdded)
                accountErrors.Add(new AccountError(code, description));
        }

        return accountErrors;
    }
}

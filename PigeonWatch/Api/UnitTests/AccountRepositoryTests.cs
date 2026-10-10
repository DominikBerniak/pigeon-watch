using Microsoft.AspNetCore.Identity;
using NSubstitute;
using PigeonWatch.BusinessObjects;
using PigeonWatch.Data.Identity;
using PigeonWatch.Data.Mappers;
using PigeonWatch.Data.Repositories;

namespace PigeonWatch.UnitTests;

public class AccountRepositoryTests
{
    private readonly UserManager<ApplicationUser> userManager = Substitute.For<UserManager<ApplicationUser>>(
        Substitute.For<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);

    [Fact]
    public async Task Successful_creation_uses_the_email_as_user_name()
    {
        userManager.CreateAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>()).Returns(IdentityResult.Success);

        AccountCreationResult result = await new AccountRepository(userManager, new UserAccountMapper()).CreateAsync(
            new NewAccount("user@example.com", "Secret1!", "Pidgey"),
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(new RegisteredAccount("user@example.com", "Pidgey"), result.Account);
        await userManager.Received(1).CreateAsync(
            Arg.Is<ApplicationUser>(user =>
                user.Email == "user@example.com"
                && user.UserName == "user@example.com"
                && user.DisplayName == "Pidgey"),
            "Secret1!");
    }

    [Fact]
    public async Task Identity_errors_map_to_account_errors()
    {
        AccountCreationResult result = await CreateWithErrors("PasswordTooShort", "PasswordRequiresUpper");

        Assert.False(result.Succeeded);
        Assert.Null(result.Account);
        Assert.Equal(
            [
                new AccountError("PasswordTooShort", "PasswordTooShort description"),
                new AccountError("PasswordRequiresUpper", "PasswordRequiresUpper description")
            ],
            result.Errors);
    }

    [Fact]
    public async Task Duplicate_user_name_and_duplicate_email_collapse_to_a_single_generic_registration_failure()
    {
        AccountCreationResult result = await CreateWithErrors(AccountErrorCodes.DuplicateUserName, AccountErrorCodes.DuplicateEmail);

        Assert.Equal(
            [new AccountError(AccountErrorCodes.RegistrationFailed, "The account could not be created.")],
            result.Errors);
    }

    [Fact]
    public async Task Registration_failure_never_reveals_that_the_email_is_registered()
    {
        userManager.CreateAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>()).Returns(IdentityResult.Failed(
            new IdentityErrorDescriber().DuplicateUserName("user@example.com"),
            new IdentityErrorDescriber().DuplicateEmail("user@example.com")));

        AccountCreationResult result = await new AccountRepository(userManager, new UserAccountMapper()).CreateAsync(
            new NewAccount("user@example.com", "Secret1!", "Pidgey"),
            TestContext.Current.CancellationToken);

        Assert.DoesNotContain(result.Errors, error => error.Code.Contains("Duplicate", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Errors, error => error.Description.Contains("user@example.com", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Errors, error => error.Description.Contains("taken", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Invalid_user_name_and_invalid_email_collapse_to_a_single_invalid_email()
    {
        AccountCreationResult result = await CreateWithErrors(AccountErrorCodes.InvalidEmail, AccountErrorCodes.InvalidUserName);

        Assert.Equal([AccountErrorCodes.InvalidEmail], result.Errors.Select(error => error.Code));
    }

    [Theory]
    [InlineData(AccountErrorCodes.DuplicateUserName, AccountErrorCodes.RegistrationFailed)]
    [InlineData(AccountErrorCodes.DuplicateEmail, AccountErrorCodes.RegistrationFailed)]
    [InlineData(AccountErrorCodes.InvalidUserName, AccountErrorCodes.InvalidEmail)]
    public async Task Email_and_user_name_errors_keep_the_display_name_error_beside_them(string identityCode, string expectedCode)
    {
        AccountCreationResult result = await CreateWithErrors(identityCode, AccountErrorCodes.DuplicateDisplayName);

        Assert.Equal([expectedCode, AccountErrorCodes.DuplicateDisplayName], result.Errors.Select(error => error.Code));
    }

    [Fact]
    public async Task Current_user_is_null_for_an_unknown_id()
    {
        Guid unknownId = Guid.NewGuid();
        userManager.FindByIdAsync(unknownId.ToString()).Returns((ApplicationUser?)null);

        CurrentUser? currentUser = await new AccountRepository(userManager, new UserAccountMapper()).GetCurrentUserAsync(
            unknownId,
            TestContext.Current.CancellationToken);

        Assert.Null(currentUser);
    }

    [Fact]
    public async Task Current_user_carries_the_id_email_and_display_name()
    {
        ApplicationUser user = new() { Id = Guid.NewGuid(), Email = "user@example.com", DisplayName = "Pidgey" };
        userManager.FindByIdAsync(user.Id.ToString()).Returns(user);

        CurrentUser? currentUser = await new AccountRepository(userManager, new UserAccountMapper()).GetCurrentUserAsync(
            user.Id,
            TestContext.Current.CancellationToken);

        Assert.Equal(new CurrentUser(user.Id, "user@example.com", "Pidgey"), currentUser);
    }

    [Fact]
    public async Task Rename_updates_the_display_name_and_returns_the_updated_profile()
    {
        ApplicationUser user = new() { Id = Guid.NewGuid(), Email = "user@example.com", DisplayName = "Pidgey" };
        userManager.FindByIdAsync(user.Id.ToString()).Returns(user);
        userManager.UpdateAsync(user).Returns(IdentityResult.Success);

        ProfileUpdateResult result = await new AccountRepository(userManager, new UserAccountMapper()).UpdateDisplayNameAsync(
            user.Id,
            "Feathers",
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(new UpdatedProfile("user@example.com", "Feathers"), result.Profile);
        await userManager.Received(1).UpdateAsync(Arg.Is<ApplicationUser>(updated => updated.Id == user.Id && updated.DisplayName == "Feathers"));
    }

    [Fact]
    public async Task Rename_of_an_unknown_user_fails_with_user_not_found_without_updating()
    {
        Guid unknownId = Guid.NewGuid();
        userManager.FindByIdAsync(unknownId.ToString()).Returns((ApplicationUser?)null);

        ProfileUpdateResult result = await new AccountRepository(userManager, new UserAccountMapper()).UpdateDisplayNameAsync(
            unknownId,
            "Feathers",
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Null(result.Profile);
        Assert.Equal([AccountErrorCodes.UserNotFound], result.Errors.Select(error => error.Code));
        await userManager.DidNotReceive().UpdateAsync(Arg.Any<ApplicationUser>());
    }

    [Fact]
    public async Task Rename_passes_a_duplicate_display_name_through()
    {
        ProfileUpdateResult result = await RenameWithErrors(AccountErrorCodes.DuplicateDisplayName);

        Assert.False(result.Succeeded);
        Assert.Null(result.Profile);
        Assert.Equal(
            [new AccountError(AccountErrorCodes.DuplicateDisplayName, $"{AccountErrorCodes.DuplicateDisplayName} description")],
            result.Errors);
    }

    [Fact]
    public async Task Rename_passes_a_concurrency_failure_through()
    {
        string concurrencyCode = new IdentityErrorDescriber().ConcurrencyFailure().Code;

        ProfileUpdateResult result = await RenameWithErrors(concurrencyCode);

        Assert.False(result.Succeeded);
        Assert.Equal([concurrencyCode], result.Errors.Select(error => error.Code));
    }

    [Fact]
    public async Task Change_password_succeeds_through_the_user_manager()
    {
        ApplicationUser user = new() { Id = Guid.NewGuid(), Email = "user@example.com", DisplayName = "Pidgey" };
        userManager.FindByIdAsync(user.Id.ToString()).Returns(user);
        userManager.ChangePasswordAsync(user, "Old1!", "New1Password!").Returns(IdentityResult.Success);

        PasswordChangeResult result = await new AccountRepository(userManager, new UserAccountMapper()).ChangePasswordAsync(
            user.Id,
            "Old1!",
            "New1Password!",
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Errors);
        await userManager.Received(1).ChangePasswordAsync(user, "Old1!", "New1Password!");
    }

    [Fact]
    public async Task Change_password_of_an_unknown_user_fails_with_user_not_found_without_changing()
    {
        Guid unknownId = Guid.NewGuid();
        userManager.FindByIdAsync(unknownId.ToString()).Returns((ApplicationUser?)null);

        PasswordChangeResult result = await new AccountRepository(userManager, new UserAccountMapper()).ChangePasswordAsync(
            unknownId,
            "Old1!",
            "New1Password!",
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal([AccountErrorCodes.UserNotFound], result.Errors.Select(error => error.Code));
        await userManager.DidNotReceive().ChangePasswordAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Change_password_passes_a_wrong_current_password_through_as_password_mismatch()
    {
        PasswordChangeResult result = await ChangePasswordWithErrors(new IdentityErrorDescriber().PasswordMismatch());

        Assert.False(result.Succeeded);
        Assert.Equal([AccountErrorCodes.PasswordMismatch], result.Errors.Select(error => error.Code));
    }

    [Fact]
    public async Task Change_password_passes_every_policy_violation_through_as_its_identity_code()
    {
        IdentityErrorDescriber describer = new();

        PasswordChangeResult result = await ChangePasswordWithErrors(
            describer.PasswordTooShort(8),
            describer.PasswordRequiresDigit(),
            describer.PasswordRequiresLower(),
            describer.PasswordRequiresUpper(),
            describer.PasswordRequiresNonAlphanumeric());

        Assert.False(result.Succeeded);
        Assert.Equal(
            [
                AccountErrorCodes.PasswordTooShort,
                AccountErrorCodes.PasswordRequiresDigit,
                AccountErrorCodes.PasswordRequiresLower,
                AccountErrorCodes.PasswordRequiresUpper,
                AccountErrorCodes.PasswordRequiresNonAlphanumeric
            ],
            result.Errors.Select(error => error.Code));
    }

    [Fact]
    public async Task Change_password_errors_never_carry_the_submitted_passwords()
    {
        IdentityErrorDescriber describer = new();

        PasswordChangeResult result = await ChangePasswordWithErrors(describer.PasswordMismatch(), describer.PasswordTooShort(8));

        Assert.DoesNotContain(result.Errors, error => error.Description.Contains("Old1!", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Errors, error => error.Description.Contains("New1Password!", StringComparison.Ordinal));
    }

    private Task<PasswordChangeResult> ChangePasswordWithErrors(params IdentityError[] errors)
    {
        ApplicationUser user = new() { Id = Guid.NewGuid(), Email = "user@example.com", DisplayName = "Pidgey" };
        userManager.FindByIdAsync(user.Id.ToString()).Returns(user);
        userManager.ChangePasswordAsync(user, "Old1!", "New1Password!").Returns(IdentityResult.Failed(errors));

        return new AccountRepository(userManager, new UserAccountMapper()).ChangePasswordAsync(
            user.Id,
            "Old1!",
            "New1Password!",
            TestContext.Current.CancellationToken);
    }

    private Task<ProfileUpdateResult> RenameWithErrors(params string[] codes)
    {
        ApplicationUser user = new() { Id = Guid.NewGuid(), Email = "user@example.com", DisplayName = "Pidgey" };
        IdentityError[] errors = codes
            .Select(code => new IdentityError { Code = code, Description = $"{code} description" })
            .ToArray();
        userManager.FindByIdAsync(user.Id.ToString()).Returns(user);
        userManager.UpdateAsync(user).Returns(IdentityResult.Failed(errors));

        return new AccountRepository(userManager, new UserAccountMapper()).UpdateDisplayNameAsync(
            user.Id,
            "Feathers",
            TestContext.Current.CancellationToken);
    }

    private Task<AccountCreationResult> CreateWithErrors(params string[] codes)
    {
        IdentityError[] errors = codes
            .Select(code => new IdentityError { Code = code, Description = $"{code} description" })
            .ToArray();
        userManager.CreateAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>()).Returns(IdentityResult.Failed(errors));

        return new AccountRepository(userManager, new UserAccountMapper()).CreateAsync(
            new NewAccount("user@example.com", "Secret1!", "Pidgey"),
            TestContext.Current.CancellationToken);
    }
}

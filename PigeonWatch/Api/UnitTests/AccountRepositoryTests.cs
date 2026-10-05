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

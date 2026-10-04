using Microsoft.AspNetCore.Identity;
using NSubstitute;
using PigeonWatch.BusinessObjects;
using PigeonWatch.Data.Identity;
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

        AccountCreationResult result = await new AccountRepository(userManager).CreateAsync(
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
    public async Task Duplicate_user_name_and_duplicate_email_collapse_to_a_single_duplicate_email()
    {
        AccountCreationResult result = await CreateWithErrors(AccountErrorCodes.DuplicateUserName, AccountErrorCodes.DuplicateEmail);

        Assert.Equal([AccountErrorCodes.DuplicateEmail], result.Errors.Select(error => error.Code));
    }

    [Fact]
    public async Task Invalid_user_name_and_invalid_email_collapse_to_a_single_invalid_email()
    {
        AccountCreationResult result = await CreateWithErrors(AccountErrorCodes.InvalidEmail, AccountErrorCodes.InvalidUserName);

        Assert.Equal([AccountErrorCodes.InvalidEmail], result.Errors.Select(error => error.Code));
    }

    [Theory]
    [InlineData(AccountErrorCodes.DuplicateUserName, AccountErrorCodes.DuplicateEmail)]
    [InlineData(AccountErrorCodes.InvalidUserName, AccountErrorCodes.InvalidEmail)]
    public async Task User_name_errors_alone_are_renamed_to_email_errors(string identityCode, string expectedCode)
    {
        AccountCreationResult result = await CreateWithErrors(identityCode, AccountErrorCodes.DuplicateDisplayName);

        Assert.Equal([expectedCode, AccountErrorCodes.DuplicateDisplayName], result.Errors.Select(error => error.Code));
    }

    private Task<AccountCreationResult> CreateWithErrors(params string[] codes)
    {
        IdentityError[] errors = codes
            .Select(code => new IdentityError { Code = code, Description = $"{code} description" })
            .ToArray();
        userManager.CreateAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>()).Returns(IdentityResult.Failed(errors));

        return new AccountRepository(userManager).CreateAsync(
            new NewAccount("user@example.com", "Secret1!", "Pidgey"),
            TestContext.Current.CancellationToken);
    }
}

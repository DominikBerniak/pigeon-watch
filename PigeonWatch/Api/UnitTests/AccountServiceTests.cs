using NSubstitute;
using PigeonWatch.BusinessLogic.Services;
using PigeonWatch.BusinessObjects;
using PigeonWatch.Data.Repositories;

namespace PigeonWatch.UnitTests;

public class AccountServiceTests
{
    private readonly IAccountRepository accountRepository = Substitute.For<IAccountRepository>();

    public AccountServiceTests()
    {
        accountRepository
            .CreateAsync(Arg.Any<NewAccount>(), Arg.Any<CancellationToken>())
            .Returns(call => AccountCreationResult.Success(
                new RegisteredAccount(call.Arg<NewAccount>().Email, call.Arg<NewAccount>().DisplayName)));
        accountRepository
            .UpdateDisplayNameAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => ProfileUpdateResult.Success(new UpdatedProfile("user@example.com", call.Arg<string>())));
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("  ab  ")]
    [InlineData("abcdefghijklmnopqrstuvwxyz12345")]
    public async Task Display_name_outside_length_bounds_is_rejected_without_calling_the_repository(string displayName)
    {
        AccountCreationResult result = await CreateService().RegisterAsync(
            new NewAccount("user@example.com", "Secret1!", displayName),
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal([AccountErrorCodes.DisplayNameLength], result.Errors.Select(error => error.Code));
        await accountRepository.DidNotReceive().CreateAsync(Arg.Any<NewAccount>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("abcdefghijklmnopqrstuvwxyz1234")]
    public async Task Display_name_within_length_bounds_is_accepted(string displayName)
    {
        AccountCreationResult result = await CreateService().RegisterAsync(
            new NewAccount("user@example.com", "Secret1!", displayName),
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        await accountRepository.Received(1).CreateAsync(
            Arg.Is<NewAccount>(account => account.DisplayName == displayName),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Display_name_with_at_sign_is_rejected_without_calling_the_repository()
    {
        AccountCreationResult result = await CreateService().RegisterAsync(
            new NewAccount("user@example.com", "Secret1!", "a@b"),
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal([AccountErrorCodes.DisplayNameInvalidCharacter], result.Errors.Select(error => error.Code));
        await accountRepository.DidNotReceive().CreateAsync(Arg.Any<NewAccount>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Email_longer_than_the_column_is_rejected_without_calling_the_repository()
    {
        AccountCreationResult result = await CreateService().RegisterAsync(
            new NewAccount($" {EmailOfLength(AccountRules.EmailMaxLength + 1)} ", "Secret1!", "Pidgey"),
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal([AccountErrorCodes.InvalidEmail], result.Errors.Select(error => error.Code));
        await accountRepository.DidNotReceive().CreateAsync(Arg.Any<NewAccount>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Email_at_the_column_length_is_accepted()
    {
        string email = EmailOfLength(AccountRules.EmailMaxLength);

        AccountCreationResult result = await CreateService().RegisterAsync(
            new NewAccount($" {email} ", "Secret1!", "Pidgey"),
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        await accountRepository.Received(1).CreateAsync(
            Arg.Is<NewAccount>(account => account.Email == email),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Email_and_display_name_are_trimmed_before_reaching_the_repository()
    {
        AccountCreationResult result = await CreateService().RegisterAsync(
            new NewAccount("  user@example.com ", " Secret1! ", "  Pidgey  "),
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(new RegisteredAccount("user@example.com", "Pidgey"), result.Account);
        await accountRepository.Received(1).CreateAsync(
            new NewAccount("user@example.com", " Secret1! ", "Pidgey"),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("a")]
    [InlineData("ab")]
    [InlineData("abcdefghijklmnopqrstuvwxyz12345")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Rename_to_a_display_name_outside_length_bounds_is_rejected_without_calling_the_repository(string displayName)
    {
        ProfileUpdateResult result = await CreateService().UpdateDisplayNameAsync(
            Guid.NewGuid(),
            displayName,
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Null(result.Profile);
        Assert.Equal([AccountErrorCodes.DisplayNameLength], result.Errors.Select(error => error.Code));
        await accountRepository.DidNotReceive().UpdateDisplayNameAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("abcdefghijklmnopqrstuvwxyz1234")]
    public async Task Rename_to_a_display_name_within_length_bounds_reaches_the_repository(string displayName)
    {
        Guid userId = Guid.NewGuid();

        ProfileUpdateResult result = await CreateService().UpdateDisplayNameAsync(
            userId,
            displayName,
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        await accountRepository.Received(1).UpdateDisplayNameAsync(userId, displayName, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Rename_to_a_display_name_with_at_sign_is_rejected_without_calling_the_repository()
    {
        ProfileUpdateResult result = await CreateService().UpdateDisplayNameAsync(
            Guid.NewGuid(),
            "a@b",
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal([AccountErrorCodes.DisplayNameInvalidCharacter], result.Errors.Select(error => error.Code));
        await accountRepository.DidNotReceive().UpdateDisplayNameAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Rename_trims_the_display_name_before_validating_and_reaching_the_repository()
    {
        Guid userId = Guid.NewGuid();

        ProfileUpdateResult result = await CreateService().UpdateDisplayNameAsync(
            userId,
            "  Pidgey  ",
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(new UpdatedProfile("user@example.com", "Pidgey"), result.Profile);
        await accountRepository.Received(1).UpdateDisplayNameAsync(userId, "Pidgey", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Rename_returns_the_repository_failure_unchanged()
    {
        AccountError duplicate = new(AccountErrorCodes.DuplicateDisplayName, "taken");
        accountRepository
            .UpdateDisplayNameAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ProfileUpdateResult.Failure([duplicate]));

        ProfileUpdateResult result = await CreateService().UpdateDisplayNameAsync(
            Guid.NewGuid(),
            "Pidgey",
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal([duplicate], result.Errors);
    }

    private AccountService CreateService()
    {
        return new(accountRepository);
    }

    private static string EmailOfLength(int length)
    {
        const string domain = "@example.com";

        return new string('a', length - domain.Length) + domain;
    }
}

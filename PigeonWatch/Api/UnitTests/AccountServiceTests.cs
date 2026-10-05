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

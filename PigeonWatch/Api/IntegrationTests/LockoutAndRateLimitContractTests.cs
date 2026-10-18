using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace PigeonWatch.IntegrationTests;

public sealed class LockoutAndRateLimitContractTests : IDisposable
{
    private const string email = "lock.target@example.com";
    private const string displayName = "LockTarget";
    private const string password = "Passw0rd!one";
    private const string wrongPassword = "Passw0rd!wrong";
    private const string newPassword = "Passw0rd!newer";

    private readonly PigeonWatchApiFactory factory = new();
    private readonly List<IDisposable> disposables = [];
    private readonly CancellationToken cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Lockout_holds_across_five_different_client_addresses()
    {
        await SeedAccountAsync();

        await FailLoginFromFiveFreshAddressesAsync();

        using HttpResponseMessage correct = await LoginFromFreshAddressAsync(password);

        Assert.Equal(HttpStatusCode.Unauthorized, correct.StatusCode);
        Assert.Equal("LockedOut", await ReadDetailAsync(correct));
    }

    [Fact]
    public async Task Lockout_expiry_lets_the_correct_password_in_again()
    {
        await SeedAccountAsync();
        await FailLoginFromFiveFreshAddressesAsync();

        using HttpResponseMessage locked = await LoginFromFreshAddressAsync(password);
        int expired = factory.ExpireLockout(email);
        using HttpResponseMessage afterExpiry = await LoginFromFreshAddressAsync(password);

        Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);
        Assert.Equal("LockedOut", await ReadDetailAsync(locked));
        Assert.Equal(1, expired);
        Assert.Equal(HttpStatusCode.OK, afterExpiry.StatusCode);
    }

    [Fact]
    public async Task Eleventh_login_request_from_one_address_is_limited_while_another_address_still_reaches_lockout()
    {
        await SeedAccountAsync();
        HttpClient limitedClient = TrackClient(factory.CreateApiClient());
        AccountApiClient limitedAccounts = new(limitedClient);

        for (int attemptNumber = 0; attemptNumber < 10; attemptNumber++)
        {
            using HttpResponseMessage attempt = await limitedAccounts.LoginAsync(email, wrongPassword, cancellationToken);

            Assert.Equal(HttpStatusCode.Unauthorized, attempt.StatusCode);
        }

        using HttpResponseMessage eleventh = await limitedAccounts.LoginAsync(email, wrongPassword, cancellationToken);
        string eleventhBody = await eleventh.Content.ReadAsStringAsync(cancellationToken);
        using HttpResponseMessage otherAddress = await LoginFromFreshAddressAsync(password);

        Assert.Equal(HttpStatusCode.TooManyRequests, eleventh.StatusCode);
        Assert.Equal(string.Empty, eleventhBody);
        Assert.Equal(HttpStatusCode.Unauthorized, otherAddress.StatusCode);
        Assert.Equal("LockedOut", await ReadDetailAsync(otherAddress));
    }

    [Fact]
    public async Task Password_guessing_with_a_stolen_access_token_is_limited_on_the_eleventh_attempt()
    {
        TestUser user = await factory.CreateUserAsync(email, password, displayName, cancellationToken);
        disposables.Add(user);
        AccountApiClient accounts = new(user.Client);

        for (int attemptNumber = 0; attemptNumber < 10; attemptNumber++)
        {
            using HttpResponseMessage guess = await GuessCurrentPasswordAsync(user);
            string[] errorCodes = await accounts.ReadErrorCodesAsync(guess, cancellationToken);

            Assert.Equal(HttpStatusCode.BadRequest, guess.StatusCode);
            Assert.Equal(["PasswordMismatch"], errorCodes);
        }

        using HttpResponseMessage eleventh = await GuessCurrentPasswordAsync(user);

        Assert.Equal(HttpStatusCode.TooManyRequests, eleventh.StatusCode);
    }

    public void Dispose()
    {
        foreach (IDisposable disposable in disposables)
            disposable.Dispose();

        factory.Dispose();
    }

    private HttpClient TrackClient(HttpClient client)
    {
        disposables.Add(client);

        return client;
    }

    private async Task SeedAccountAsync()
    {
        using HttpClient client = factory.CreateApiClient();
        using HttpResponseMessage register = await new AccountApiClient(client).RegisterAsync(email, password, displayName, cancellationToken);

        register.EnsureSuccessStatusCode();
    }

    private async Task FailLoginFromFiveFreshAddressesAsync()
    {
        for (int attemptNumber = 0; attemptNumber < 5; attemptNumber++)
        {
            using HttpResponseMessage attempt = await LoginFromFreshAddressAsync(wrongPassword);

            Assert.Equal(HttpStatusCode.Unauthorized, attempt.StatusCode);
        }
    }

    private async Task<HttpResponseMessage> LoginFromFreshAddressAsync(string loginPassword)
    {
        using HttpClient client = factory.CreateApiClient();
        HttpResponseMessage response = await new AccountApiClient(client).LoginAsync(email, loginPassword, cancellationToken);

        return response;
    }

    private async Task<HttpResponseMessage> GuessCurrentPasswordAsync(TestUser user)
    {
        HttpResponseMessage response = await user.Client.PutAsJsonAsync(
            "/account/password",
            new { currentPassword = wrongPassword, newPassword },
            cancellationToken);

        return response;
    }

    private async Task<string> ReadDetailAsync(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        using JsonDocument document = JsonDocument.Parse(body);
        string detail = document.RootElement.GetProperty("detail").GetString()!;

        return detail;
    }
}

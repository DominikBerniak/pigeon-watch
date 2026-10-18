using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace PigeonWatch.IntegrationTests;

public sealed class TokenLifecycleContractTests : IDisposable
{
    private const string email = "token.holder@example.com";
    private const string displayName = "TokenHolder";
    private const string password = "Passw0rd!token1";
    private const string newPassword = "Passw0rd!token2";

    private readonly ManualTimeProvider clock = new(DateTimeOffset.UtcNow);
    private readonly PigeonWatchApiFactory factory;
    private readonly List<TestUser> users = [];
    private readonly CancellationToken cancellationToken = TestContext.Current.CancellationToken;

    public TokenLifecycleContractTests()
    {
        factory = new PigeonWatchApiFactory(clock);
    }

    [Fact]
    public async Task Refresh_without_a_password_change_returns_working_tokens()
    {
        TestUser user = await SeedUserAsync();

        using HttpResponseMessage refresh = await RefreshAsync(user.RefreshToken);
        string refreshBody = await refresh.Content.ReadAsStringAsync(cancellationToken);
        using JsonDocument refreshed = JsonDocument.Parse(refreshBody);
        string refreshedAccessToken = refreshed.RootElement.GetProperty("accessToken").GetString()!;
        string refreshedRefreshToken = refreshed.RootElement.GetProperty("refreshToken").GetString()!;
        HttpStatusCode refreshedRead = await ReadGeneralStatusAsync(refreshedAccessToken);

        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(refreshedAccessToken));
        Assert.False(string.IsNullOrWhiteSpace(refreshedRefreshToken));
        Assert.Equal(HttpStatusCode.OK, refreshedRead);
    }

    [Fact]
    public async Task Rename_keeps_other_sessions_alive_because_it_does_not_rotate_the_security_stamp()
    {
        TestUser user = await SeedUserAsync();

        using HttpResponseMessage rename = await user.Client.PutAsJsonAsync(
            "/account/profile",
            new { displayName = "TokenRenamed" },
            cancellationToken);
        using HttpResponseMessage refresh = await RefreshAsync(user.RefreshToken);
        HttpStatusCode oldAccessRead = await ReadGeneralStatusAsync(user.AccessToken);

        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        Assert.Equal(HttpStatusCode.OK, oldAccessRead);
    }

    [Fact]
    public async Task Password_change_rejects_the_old_refresh_token_and_the_new_password_logs_in()
    {
        TestUser user = await SeedUserAsync();

        using HttpResponseMessage change = await ChangePasswordAsync(user);
        using HttpResponseMessage oldRefresh = await RefreshAsync(user.RefreshToken);
        HttpStatusCode oldPasswordLogin = await LoginAsync(password);
        HttpStatusCode newPasswordLogin = await LoginAsync(newPassword);

        Assert.Equal(HttpStatusCode.OK, change.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, oldRefresh.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, oldPasswordLogin);
        Assert.Equal(HttpStatusCode.OK, newPasswordLogin);
    }

    [Fact]
    public async Task Old_access_token_survives_a_password_change_until_its_own_expiry_by_design()
    {
        TestUser user = await SeedUserAsync();

        using HttpResponseMessage change = await ChangePasswordAsync(user);
        HttpStatusCode oldAccessRead = await ReadGeneralStatusAsync(user.AccessToken);

        Assert.Equal(HttpStatusCode.OK, change.StatusCode);
        Assert.Equal(HttpStatusCode.OK, oldAccessRead);
    }

    [Fact]
    public async Task Old_access_token_is_rejected_after_expiry_of_its_lifetime()
    {
        TestUser user = await SeedUserAsync();

        using HttpResponseMessage change = await ChangePasswordAsync(user);
        HttpStatusCode beforeExpiry = await ReadGeneralStatusAsync(user.AccessToken);
        clock.Advance(TimeSpan.FromMinutes(61));
        HttpStatusCode afterExpiry = await ReadGeneralStatusAsync(user.AccessToken);

        Assert.Equal(HttpStatusCode.OK, change.StatusCode);
        Assert.Equal(HttpStatusCode.OK, beforeExpiry);
        Assert.Equal(HttpStatusCode.Unauthorized, afterExpiry);
    }

    public void Dispose()
    {
        foreach (TestUser user in users)
            user.Dispose();

        factory.Dispose();
    }

    private async Task<TestUser> SeedUserAsync()
    {
        TestUser user = await factory.CreateUserAsync(email, password, displayName, cancellationToken);
        users.Add(user);

        return user;
    }

    private async Task<HttpResponseMessage> ChangePasswordAsync(TestUser user)
    {
        HttpResponseMessage response = await user.Client.PutAsJsonAsync(
            "/account/password",
            new { currentPassword = password, newPassword },
            cancellationToken);

        return response;
    }

    private async Task<HttpResponseMessage> RefreshAsync(string refreshToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, "/auth/refresh")
        {
            Content = JsonContent.Create(new { refreshToken })
        };
        HttpResponseMessage response = await factory.SendFromFreshAddressAsync(request, null, cancellationToken);

        return response;
    }

    private async Task<HttpStatusCode> ReadGeneralStatusAsync(string accessToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, "/configuration/general");
        using HttpResponseMessage response = await factory.SendFromFreshAddressAsync(request, accessToken, cancellationToken);

        return response.StatusCode;
    }

    private async Task<HttpStatusCode> LoginAsync(string loginPassword)
    {
        using HttpClient client = factory.CreateApiClient();
        using HttpResponseMessage response = await new AccountApiClient(client).LoginAsync(email, loginPassword, cancellationToken);

        return response.StatusCode;
    }
}

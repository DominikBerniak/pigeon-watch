using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace PigeonWatch.IntegrationTests;

public sealed class AuthEventLoggingTests : IDisposable
{
    private const string email = "auth.events@example.com";
    private const string password = "Passw0rd!events1";
    private const string wrongPassword = "Passw0rd!wrong1";
    private const string displayName = "AuthEvents";

    private readonly PigeonWatchApiFactory factory = new();
    private readonly CancellationToken cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Successful_login_logs_succeeded_at_information_without_secrets()
    {
        await SeedAccountAsync();
        factory.ClearLogs();

        using HttpResponseMessage login = await LoginAsync(password);
        string body = await login.Content.ReadAsStringAsync(cancellationToken);
        using JsonDocument tokens = JsonDocument.Parse(body);
        string accessToken = tokens.RootElement.GetProperty("accessToken").GetString()!;
        string refreshToken = tokens.RootElement.GetProperty("refreshToken").GetString()!;
        List<FakeLogRecord> events = AuthEvents();

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        FakeLogRecord record = Assert.Single(events);
        Assert.Equal(LogLevel.Information, record.Level);
        Assert.Equal("Succeeded", State(record, "Outcome"));
        Assert.Equal("auth/login", State(record, "Route"));
        AssertNoSecrets(accessToken, refreshToken);
    }

    [Fact]
    public async Task Wrong_password_login_logs_failed_at_warning_without_secrets()
    {
        await SeedAccountAsync();
        factory.ClearLogs();

        using HttpResponseMessage login = await LoginAsync(wrongPassword);
        List<FakeLogRecord> events = AuthEvents();

        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        FakeLogRecord record = Assert.Single(events);
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal("Failed", State(record, "Outcome"));
        Assert.Equal("auth/login", State(record, "Route"));
        AssertNoSecrets();
    }

    [Fact]
    public async Task Login_after_five_failures_logs_locked_out_at_warning()
    {
        await SeedAccountAsync();

        for (int attemptNumber = 0; attemptNumber < 5; attemptNumber++)
        {
            using HttpResponseMessage attempt = await LoginAsync(wrongPassword);

            Assert.Equal(HttpStatusCode.Unauthorized, attempt.StatusCode);
        }

        factory.ClearLogs();

        using HttpResponseMessage locked = await LoginAsync(password);
        List<FakeLogRecord> events = AuthEvents();

        Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);
        FakeLogRecord record = Assert.Single(events);
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal("LockedOut", State(record, "Outcome"));
        AssertNoSecrets();
    }

    [Fact]
    public async Task Refresh_with_an_invalid_token_logs_rejected_at_warning()
    {
        factory.ClearLogs();

        using HttpRequestMessage request = new(HttpMethod.Post, "/auth/refresh")
        {
            Content = JsonContent("{\"refreshToken\":\"not-a-real-token-value\"}")
        };
        using HttpResponseMessage refresh = await factory.SendFromFreshAddressAsync(request, null, cancellationToken);
        List<FakeLogRecord> events = AuthEvents();

        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        FakeLogRecord record = Assert.Single(events);
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal("Rejected", State(record, "Outcome"));
        Assert.Equal("auth/refresh", State(record, "Route"));
        AssertNoSecrets("not-a-real-token-value");
    }

    [Fact]
    public async Task Refresh_with_a_valid_token_logs_succeeded_at_information()
    {
        TestUser user = await factory.CreateUserAsync(email, password, displayName, cancellationToken);
        factory.ClearLogs();

        using HttpRequestMessage request = new(HttpMethod.Post, "/auth/refresh")
        {
            Content = JsonContent($"{{\"refreshToken\":\"{user.RefreshToken}\"}}")
        };
        using HttpResponseMessage refresh = await factory.SendFromFreshAddressAsync(request, null, cancellationToken);
        List<FakeLogRecord> events = AuthEvents();
        user.Dispose();

        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        FakeLogRecord record = Assert.Single(events);
        Assert.Equal(LogLevel.Information, record.Level);
        Assert.Equal("Succeeded", State(record, "Outcome"));
        AssertNoSecrets(user.RefreshToken, user.AccessToken);
    }

    [Fact]
    public async Task Eleventh_login_in_a_minute_logs_one_rate_limit_warning_and_keeps_the_empty_429()
    {
        await SeedAccountAsync();
        using HttpClient client = factory.CreateApiClient();
        AccountApiClient accounts = new(client);

        for (int attemptNumber = 0; attemptNumber < 10; attemptNumber++)
        {
            using HttpResponseMessage attempt = await accounts.LoginAsync(email, wrongPassword, cancellationToken);

            Assert.Equal(HttpStatusCode.Unauthorized, attempt.StatusCode);
        }

        factory.ClearLogs();

        using HttpResponseMessage eleventh = await accounts.LoginAsync(email, wrongPassword, cancellationToken);
        string eleventhBody = await eleventh.Content.ReadAsStringAsync(cancellationToken);
        List<FakeLogRecord> rejections = factory.Logs.GetSnapshot()
            .Where(record => record.Category == "PigeonWatch.RateLimiting")
            .ToList();

        Assert.Equal(HttpStatusCode.TooManyRequests, eleventh.StatusCode);
        Assert.Equal(string.Empty, eleventhBody);
        FakeLogRecord rejection = Assert.Single(rejections);
        Assert.Equal(LogLevel.Warning, rejection.Level);
        Assert.Equal("auth", State(rejection, "PolicyName"));
        Assert.Equal("auth/login", State(rejection, "Route"));
        Assert.Equal("False", State(rejection, "RemoteIpMissing"));
        Assert.DoesNotContain(
            factory.Logs.GetSnapshot(),
            record => record.Message.Contains("10.0.", StringComparison.Ordinal)
                || record.Message.Contains(email, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Stubbed_identity_route_logs_one_information_and_returns_the_same_empty_404()
    {
        factory.ClearLogs();

        using HttpClient client = factory.CreateApiClient();
        using HttpResponseMessage response = await client.PostAsync("/auth/forgotPassword", JsonContent("{}"), cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        List<FakeLogRecord> hits = AuthEvents();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(string.Empty, body);
        FakeLogRecord hit = Assert.Single(hits);
        Assert.Equal(LogLevel.Information, hit.Level);
        Assert.Equal("auth/forgotPassword", State(hit, "Route"));
        Assert.Equal("POST", State(hit, "Method"));
    }

    public void Dispose()
    {
        factory.Dispose();
    }

    private static StringContent JsonContent(string json)
    {
        return new StringContent(json, System.Text.Encoding.UTF8, "application/json");
    }

    private static string? State(FakeLogRecord record, string key)
    {
        string? value = record.StructuredState?.FirstOrDefault(pair => pair.Key == key).Value;

        return value;
    }

    private List<FakeLogRecord> AuthEvents()
    {
        List<FakeLogRecord> events = factory.Logs.GetSnapshot()
            .Where(record => record.Category == "PigeonWatch.AuthEvents")
            .ToList();

        return events;
    }

    private void AssertNoSecrets(params string[] secrets)
    {
        string[] forbidden = [email, password, wrongPassword, displayName, .. secrets];

        foreach (FakeLogRecord record in factory.Logs.GetSnapshot())
        {
            string text = record.Message + " " + string.Join(' ', record.StructuredState?.Select(pair => pair.Value) ?? []);

            foreach (string secret in forbidden)
                Assert.DoesNotContain(secret, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    private async Task SeedAccountAsync()
    {
        using HttpClient client = factory.CreateApiClient();
        using HttpResponseMessage register = await new AccountApiClient(client).RegisterAsync(email, password, displayName, cancellationToken);

        register.EnsureSuccessStatusCode();
    }

    private async Task<HttpResponseMessage> LoginAsync(string loginPassword)
    {
        using HttpClient client = factory.CreateApiClient();
        HttpResponseMessage response = await new AccountApiClient(client).LoginAsync(email, loginPassword, cancellationToken);

        return response;
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace PigeonWatch.IntegrationTests;

public sealed class ControllerFailureLoggingTests : IDisposable
{
    private const string email = "failure.user@example.com";
    private const string password = "Passw0rd!failure1";
    private const string displayName = "FailureUser";

    private readonly ManualTimeProvider clock = new(DateTimeOffset.UtcNow);
    private readonly PigeonWatchApiFactory factory;
    private readonly CancellationToken cancellationToken = TestContext.Current.CancellationToken;

    public ControllerFailureLoggingTests()
    {
        factory = new PigeonWatchApiFactory(clock);
    }

    [Fact]
    public async Task Token_for_a_deleted_user_logs_a_warning_with_the_user_id_and_returns_the_bare_401()
    {
        using TestUser user = await factory.CreateUserAsync(email, password, displayName, cancellationToken);
        factory.DeleteUserAccount(user.Id);
        factory.ClearLogs();

        using HttpResponseMessage response = await user.Client.PostAsJsonAsync("/sightings", ValidSighting(), cancellationToken);

        FakeLogRecord warning = Assert.Single(factory.Logs.GetSnapshot(), record => record.Level == LogLevel.Warning);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(401, await ReadProblemStatusAsync(response));
        Assert.Equal(user.Id.ToString(), ScopeValue(warning, "UserId"));
        Assert.Equal("sightings", State(warning, "Route"));
    }

    [Fact]
    public async Task Invalid_location_logs_a_warning_with_the_error_code_and_returns_the_same_400_body()
    {
        using TestUser user = await factory.CreateUserAsync(email, password, displayName, cancellationToken);
        factory.ClearLogs();

        using HttpResponseMessage response = await user.Client.PostAsJsonAsync("/sightings", ValidSighting(latitude: 200), cancellationToken);

        FakeLogRecord warning = Assert.Single(factory.Logs.GetSnapshot(), record => record.Level == LogLevel.Warning);
        string[] codes = await new AccountApiClient(user.Client).ReadErrorCodesAsync(response, cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["InvalidLocation"], codes);
        Assert.Equal("InvalidLocation", State(warning, "ErrorCode"));
        Assert.Equal(user.Id.ToString(), ScopeValue(warning, "UserId"));
        Assert.DoesNotContain(email, warning.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Cooldown_rejection_logs_no_warning_or_error()
    {
        using TestUser user = await factory.CreateUserAsync(email, password, displayName, cancellationToken);
        using HttpResponseMessage first = await user.Client.PostAsJsonAsync("/sightings", ValidSighting(), cancellationToken);
        first.EnsureSuccessStatusCode();
        factory.ClearLogs();

        using HttpResponseMessage second = await user.Client.PostAsJsonAsync("/sightings", ValidSighting(), cancellationToken);

        string[] codes = await new AccountApiClient(user.Client).ReadErrorCodesAsync(second, cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        Assert.Equal(["CooldownActive"], codes);
        Assert.DoesNotContain(factory.Logs.GetSnapshot(), record => record.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task Missing_sighting_logs_one_information_with_the_id_and_returns_the_same_404()
    {
        using TestUser user = await factory.CreateUserAsync(email, password, displayName, cancellationToken);
        Guid missingId = Guid.NewGuid();
        factory.ClearLogs();

        using HttpResponseMessage response = await user.Client.GetAsync($"/sightings/{missingId}", cancellationToken);

        FakeLogRecord information = Assert.Single(
            factory.Logs.GetSnapshot(),
            record => record.StructuredState?.Any(pair => pair.Key == "SightingId") == true);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(404, await ReadProblemStatusAsync(response));
        Assert.Equal(LogLevel.Information, information.Level);
        Assert.Equal(missingId.ToString(), State(information, "SightingId"));
    }

    public void Dispose()
    {
        factory.Dispose();
    }

    private async Task<int> ReadProblemStatusAsync(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        using JsonDocument problem = JsonDocument.Parse(body);
        int status = problem.RootElement.GetProperty("status").GetInt32();

        return status;
    }

    private static object ValidSighting(double latitude = 52.2297)
    {
        return new
        {
            name = "Grey",
            description = "Limping near the fountain",
            behaviour = "Cannot fly",
            latitude,
            longitude = 21.0122,
            status = 1,
            criticality = 2
        };
    }

    private static string? State(FakeLogRecord record, string key)
    {
        string? value = record.StructuredState?.FirstOrDefault(pair => pair.Key == key).Value;

        return value;
    }

    private static string? ScopeValue(FakeLogRecord record, string key)
    {
        foreach (object? scope in record.Scopes)
        {
            if (scope is IEnumerable<KeyValuePair<string, object?>> pairs)
            {
                foreach (KeyValuePair<string, object?> pair in pairs)
                {
                    if (pair.Key == key)
                        return pair.Value?.ToString();
                }
            }
        }

        return null;
    }
}

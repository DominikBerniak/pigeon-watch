using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace PigeonWatch.IntegrationTests;

public sealed class IdentityFailureLoggingTests : IDisposable
{
    private const string email = "identity.user@example.com";
    private const string password = "Passw0rd!identity1";
    private const string displayName = "IdentityUser";
    private const string otherDisplayName = "OtherIdentity";

    private readonly PigeonWatchApiFactory factory = new();
    private readonly HttpClient httpClient;
    private readonly AccountApiClient accounts;
    private readonly CancellationToken cancellationToken = TestContext.Current.CancellationToken;

    public IdentityFailureLoggingTests()
    {
        httpClient = factory.CreateApiClient();
        accounts = new AccountApiClient(httpClient);
    }

    [Fact]
    public async Task Duplicate_registration_logs_the_original_code_while_the_response_stays_generic()
    {
        (await accounts.RegisterAsync(email, password, displayName, cancellationToken)).EnsureSuccessStatusCode();
        factory.ClearLogs();

        HttpResponseMessage repeat = await accounts.RegisterAsync(email, password, otherDisplayName, cancellationToken);

        string body = await repeat.Content.ReadAsStringAsync(cancellationToken);
        string[] errorCodes = await accounts.ReadErrorCodesAsync(repeat, cancellationToken);
        List<FakeLogRecord> collapsed = factory.Logs.GetSnapshot()
            .Where(record => record.Level == LogLevel.Information && record.StructuredState?.Any(pair => pair.Key == "ErrorCode") == true)
            .ToList();
        Assert.Equal(HttpStatusCode.BadRequest, repeat.StatusCode);
        Assert.Equal(["RegistrationFailed"], errorCodes);
        Assert.DoesNotContain("Duplicate", body, StringComparison.Ordinal);
        Assert.NotEmpty(collapsed);
        Assert.Contains(collapsed, record => State(record, "ErrorCode") == "DuplicateEmail");
        Assert.All(collapsed, record => Assert.Equal("register", State(record, "Operation")));
        Assert.DoesNotContain(
            factory.Logs.GetSnapshot(),
            record => record.Message.Contains(email, StringComparison.OrdinalIgnoreCase)
                || record.Message.Contains(otherDisplayName, StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        httpClient.Dispose();
        factory.Dispose();
    }

    private static string? State(FakeLogRecord record, string key)
    {
        string? value = record.StructuredState?.FirstOrDefault(pair => pair.Key == key).Value;

        return value;
    }
}

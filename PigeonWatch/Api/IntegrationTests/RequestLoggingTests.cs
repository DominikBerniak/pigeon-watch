using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace PigeonWatch.IntegrationTests;

public sealed class RequestLoggingTests : IDisposable
{
    private const string email = "logging.user@example.com";
    private const string password = "Passw0rd!logging1";
    private const string displayName = "LoggingUser";
    private const string nearbyUri = "/sightings/nearby?latitude=52.2297&longitude=21.0122";

    private readonly PigeonWatchApiFactory factory = new();
    private readonly CancellationToken cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Every_request_logs_one_summary_line_with_method_route_status_and_duration()
    {
        using HttpClient client = factory.CreateApiClient();
        factory.ClearLogs();

        using HttpResponseMessage response = await client.GetAsync("/configuration/client", cancellationToken);

        FakeLogRecord summary = Assert.Single(SummaryLines());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(LogLevel.Information, summary.Level);
        Assert.Equal("GET", State(summary, "Method"));
        Assert.Equal("configuration/client", State(summary, "Route"));
        Assert.Equal("200", State(summary, "StatusCode"));
        Assert.True(double.Parse(State(summary, "ElapsedMs")!, System.Globalization.CultureInfo.InvariantCulture) >= 0);
    }

    [Fact]
    public async Task Request_to_an_unknown_route_logs_the_summary_line_as_unmatched()
    {
        using HttpClient client = factory.CreateApiClient();
        factory.ClearLogs();

        using HttpResponseMessage response = await client.GetAsync("/no/such/route", cancellationToken);

        FakeLogRecord summary = Assert.Single(SummaryLines());
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("unmatched", State(summary, "Route"));
        Assert.Equal("404", State(summary, "StatusCode"));
    }

    [Fact]
    public async Task Nearby_request_logs_no_coordinates_in_any_record()
    {
        using TestUser user = await factory.CreateUserAsync(email, password, displayName, cancellationToken);
        factory.ClearLogs();

        using HttpResponseMessage response = await user.Client.GetAsync(nearbyUri, cancellationToken);

        FakeLogRecord summary = Assert.Single(SummaryLines());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("sightings/nearby", State(summary, "Route"));
        Assert.All(factory.Logs.GetSnapshot(), record =>
        {
            Assert.DoesNotContain("52.2297", record.Message);
            Assert.DoesNotContain("21.0122", record.Message);
            Assert.DoesNotContain("latitude", record.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(record.StructuredState ?? [], pair => (pair.Value ?? string.Empty).Contains("52.2297"));
        });
    }

    [Fact]
    public async Task Authenticated_request_records_carry_the_user_id_and_trace_id_scope_values()
    {
        using TestUser user = await factory.CreateUserAsync(email, password, displayName, cancellationToken);
        factory.ClearLogs();

        using HttpResponseMessage response = await user.Client.GetAsync(nearbyUri, cancellationToken);

        FakeLogRecord summary = Assert.Single(SummaryLines());
        Dictionary<string, object?> scope = ScopeValues(summary);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(user.Id.ToString(), scope["UserId"]?.ToString());
        Assert.False(string.IsNullOrEmpty(scope["TraceId"]?.ToString()));
    }

    [Fact]
    public async Task Anonymous_request_scope_has_a_trace_id_and_no_user_id()
    {
        using HttpClient client = factory.CreateApiClient();
        factory.ClearLogs();

        using HttpResponseMessage response = await client.GetAsync("/configuration/client", cancellationToken);

        FakeLogRecord summary = Assert.Single(SummaryLines());
        Dictionary<string, object?> scope = ScopeValues(summary);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(scope.ContainsKey("TraceId"));
        Assert.False(scope.ContainsKey("UserId"));
    }

    [Fact]
    public async Task Server_error_logs_the_exception_and_the_summary_line_with_the_same_scope_values()
    {
        using TestUser user = await factory.CreateUserAsync(email, password, displayName, cancellationToken);
        factory.ClearLogs();
        factory.CloseDatabase();

        using HttpResponseMessage response = await user.Client.GetAsync("/sightings/mine", cancellationToken);

        FakeLogRecord summary = Assert.Single(SummaryLines());
        FakeLogRecord failure = Assert.Single(factory.Logs.GetSnapshot(), record => record.Level == LogLevel.Error && record.Exception is not null && record.Category.EndsWith("ExceptionHandlerMiddleware", StringComparison.Ordinal));
        Dictionary<string, object?> summaryScope = ScopeValues(summary);
        Dictionary<string, object?> failureScope = ScopeValues(failure);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("sightings/mine", State(summary, "Route"));
        Assert.Equal("500", State(summary, "StatusCode"));
        Assert.Equal(user.Id.ToString(), failureScope["UserId"]?.ToString());
        Assert.Equal(summaryScope["TraceId"]?.ToString(), failureScope["TraceId"]?.ToString());
        Assert.Equal(summaryScope["UserId"]?.ToString(), failureScope["UserId"]?.ToString());
    }

    [Fact]
    public async Task Entity_framework_command_lines_at_information_are_not_emitted()
    {
        using TestUser user = await factory.CreateUserAsync(email, password, displayName, cancellationToken);
        factory.ClearLogs();

        using HttpResponseMessage response = await user.Client.GetAsync("/sightings/mine", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(
            factory.Logs.GetSnapshot(),
            record => record.Category?.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) == true && record.Level < LogLevel.Warning);
    }

    public void Dispose()
    {
        factory.Dispose();
    }

    private List<FakeLogRecord> SummaryLines()
    {
        List<FakeLogRecord> lines = factory.Logs.GetSnapshot()
            .Where(record => record.StructuredState?.Any(pair => pair.Key == "StatusCode") == true)
            .ToList();

        return lines;
    }

    private static string? State(FakeLogRecord record, string key)
    {
        string? value = record.StructuredState?.FirstOrDefault(pair => pair.Key == key).Value;

        return value;
    }

    private static Dictionary<string, object?> ScopeValues(FakeLogRecord record)
    {
        Dictionary<string, object?> values = [];

        foreach (object? scope in record.Scopes)
        {
            if (scope is IEnumerable<KeyValuePair<string, object?>> pairs)
            {
                foreach (KeyValuePair<string, object?> pair in pairs)
                    values[pair.Key] = pair.Value;
            }
        }

        return values;
    }
}

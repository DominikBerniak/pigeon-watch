using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace PigeonWatch.IntegrationTests;

public sealed class ClientErrorReportTests : IDisposable
{
    private const string reportUri = "/client-errors";
    private const string clientTraceId = "4bf92f3577b34da6a3ce929d0e0e4736";

    private readonly PigeonWatchApiFactory factory = new();
    private readonly CancellationToken cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Valid_report_returns_accepted_with_no_content_and_logs_one_structured_record_with_the_request_trace_scope()
    {
        using HttpClient client = factory.CreateApiClient();
        Guid sessionId = Guid.NewGuid();
        factory.ClearLogs();

        using HttpResponseMessage response = await PostAsync(client, Body("api-failure", sessionId));
        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        FakeLogRecord record = Assert.Single(ReportRecords());
        Dictionary<string, object?> scope = ScopeValues(record);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(string.Empty, responseBody);
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal("api-failure", State(record, "Kind"));
        Assert.Equal("nearby.load", State(record, "Context"));
        Assert.Equal("500", State(record, "HttpStatus"));
        Assert.Equal("HttpErrorResponse", State(record, "ErrorName"));
        Assert.Equal("/sightings/:id", State(record, "Route"));
        Assert.Equal(sessionId.ToString(), State(record, "SessionId"));
        Assert.Equal(clientTraceId, State(record, "ClientTraceId"));
        Assert.False(string.IsNullOrEmpty(scope["TraceId"]?.ToString()));
        Assert.NotEqual(clientTraceId, scope["TraceId"]?.ToString());
        Assert.DoesNotContain(record.StructuredState ?? [], pair => pair.Key == "TraceId");
    }

    [Theory]
    [InlineData("uncaught", LogLevel.Warning)]
    [InlineData("bootstrap", LogLevel.Warning)]
    [InlineData("navigation", LogLevel.Warning)]
    [InlineData("session-expired", LogLevel.Warning)]
    [InlineData("warmup-exhausted", LogLevel.Warning)]
    [InlineData("fallback", LogLevel.Information)]
    public async Task Report_logs_at_the_level_the_server_maps_from_the_kind(string kind, LogLevel expectedLevel)
    {
        using HttpClient client = factory.CreateApiClient();
        factory.ClearLogs();

        using HttpResponseMessage response = await PostAsync(client, Body(kind));

        FakeLogRecord record = Assert.Single(ReportRecords());
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(expectedLevel, record.Level);
    }

    [Fact]
    public async Task Report_with_only_the_required_fields_is_accepted()
    {
        using HttpClient client = factory.CreateApiClient();
        string body = JsonSerializer.Serialize(new { kind = "fallback", context = "storage.unavailable", route = "/", sessionId = Guid.NewGuid() });
        factory.ClearLogs();

        using HttpResponseMessage response = await PostAsync(client, body);

        FakeLogRecord record = Assert.Single(ReportRecords());
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("storage.unavailable", State(record, "Context"));
    }

    [Theory]
    [InlineData("kind", "unknown-kind")]
    [InlineData("kind", "")]
    [InlineData("kind", "Uncaught")]
    [InlineData("context", "Has.Upper")]
    [InlineData("context", "nearby.load\nforged line")]
    [InlineData("context", "nearby.load\n")]
    [InlineData("context", "nearby load")]
    [InlineData("context", "user@example.com")]
    [InlineData("errorName", "Name With Spaces")]
    [InlineData("errorName", "Error\r\nforged")]
    [InlineData("errorName", "Error\n")]
    [InlineData("traceId", "not-hex-zzz")]
    [InlineData("traceId", "abc123\n")]
    [InlineData("route", "/sightings?email=user@example.com")]
    [InlineData("route", "/sightings/abc\nforged")]
    [InlineData("route", "/sightings/abc\n")]
    [InlineData("route", "no-leading-slash")]
    [InlineData("sessionId", "not-a-guid")]
    [InlineData("httpStatus", 600)]
    [InlineData("httpStatus", -1)]
    public async Task Report_with_a_disallowed_value_is_rejected_and_logs_nothing(string field, object value)
    {
        using HttpClient client = factory.CreateApiClient();
        factory.ClearLogs();

        using HttpResponseMessage response = await PostAsync(client, JsonSerializer.Serialize(WithField(field, value)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(ReportRecords());
    }

    [Theory]
    [InlineData("context", 61)]
    [InlineData("errorName", 81)]
    [InlineData("traceId", 65)]
    [InlineData("route", 101)]
    public async Task Report_with_an_over_long_field_is_rejected_and_logs_nothing(string field, int length)
    {
        using HttpClient client = factory.CreateApiClient();
        string value = field == "route" ? "/" + new string('a', length - 1) : new string(field == "traceId" ? 'a' : 'x', length);
        factory.ClearLogs();

        using HttpResponseMessage response = await PostAsync(client, JsonSerializer.Serialize(WithField(field, value)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(ReportRecords());
    }

    [Theory]
    [InlineData("kind")]
    [InlineData("context")]
    [InlineData("route")]
    [InlineData("sessionId")]
    public async Task Report_missing_a_required_field_is_rejected_and_logs_nothing(string field)
    {
        using HttpClient client = factory.CreateApiClient();
        Dictionary<string, object?> fields = ValidFields();
        fields.Remove(field);
        factory.ClearLogs();

        using HttpResponseMessage response = await PostAsync(client, JsonSerializer.Serialize(fields));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(ReportRecords());
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    public async Task Report_with_a_malformed_body_is_rejected_and_logs_nothing(string body)
    {
        using HttpClient client = factory.CreateApiClient();
        factory.ClearLogs();

        using HttpResponseMessage response = await PostAsync(client, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(ReportRecords());
    }

    [Fact]
    public void Report_endpoint_declares_a_request_body_cap_of_about_two_kilobytes()
    {
        EndpointDataSource dataSource = factory.Services.GetRequiredService<EndpointDataSource>();
        RouteEndpoint endpoint = Assert.Single(
            dataSource.Endpoints.OfType<RouteEndpoint>(),
            candidate => candidate.RoutePattern.RawText == "client-errors");

        IRequestSizeLimitMetadata? limit = endpoint.Metadata.GetMetadata<IRequestSizeLimitMetadata>();

        Assert.NotNull(limit);
        Assert.InRange(limit.MaxRequestBodySize ?? long.MaxValue, 1, 2048);
    }

    [Fact]
    public async Task Twenty_first_report_from_one_client_in_a_minute_gets_an_empty_429_and_logs_the_policy_warning()
    {
        using HttpClient client = factory.CreateApiClient();
        factory.ClearLogs();

        for (int attempt = 0; attempt < 20; attempt++)
        {
            using HttpResponseMessage accepted = await PostAsync(client, Body("fallback"));

            Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        }

        using HttpResponseMessage limited = await PostAsync(client, Body("fallback"));
        string limitedBody = await limited.Content.ReadAsStringAsync(cancellationToken);
        FakeLogRecord rejection = Assert.Single(
            factory.Logs.GetSnapshot(),
            record => record.Category == "PigeonWatch.RateLimiting");

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal(string.Empty, limitedBody);
        Assert.Equal(LogLevel.Warning, rejection.Level);
        Assert.Equal("clientErrors", State(rejection, "PolicyName"));
        Assert.Equal("client-errors", State(rejection, "Route"));
        Assert.Equal(20, ReportRecords().Count);
    }

    [Fact]
    public async Task Report_budget_is_per_client_address_and_separate_from_the_login_budget()
    {
        using HttpClient exhausted = factory.CreateApiClient();
        using HttpClient other = factory.CreateApiClient();

        for (int attempt = 0; attempt < 20; attempt++)
        {
            using HttpResponseMessage accepted = await PostAsync(exhausted, Body("fallback"));
        }

        using HttpResponseMessage limited = await PostAsync(exhausted, Body("fallback"));
        using HttpResponseMessage otherAddress = await PostAsync(other, Body("fallback"));
        using HttpResponseMessage login = await new AccountApiClient(exhausted).LoginAsync("nobody@example.com", "Passw0rd!none", cancellationToken);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, otherAddress.StatusCode);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, login.StatusCode);
    }

    [Fact]
    public async Task No_log_record_contains_an_email_like_string_or_a_query_string()
    {
        using HttpClient client = factory.CreateApiClient();
        factory.ClearLogs();

        using HttpResponseMessage accepted = await PostAsync(client, Body("api-failure"), "?email=leak.target@example.com&token=secret");
        using HttpResponseMessage rejected = await PostAsync(client, JsonSerializer.Serialize(WithField("context", "leak.target@example.com")), "?email=leak.target@example.com");

        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.All(factory.Logs.GetSnapshot(), record =>
        {
            Assert.DoesNotContain("@", record.Message);
            Assert.DoesNotContain("?", record.Message);
            Assert.DoesNotContain("leak.target", record.Message);
            Assert.DoesNotContain("secret", record.Message);
            Assert.DoesNotContain(
                record.StructuredState ?? [],
                pair => (pair.Value ?? string.Empty).Contains('@') || (pair.Value ?? string.Empty).Contains('?') || (pair.Value ?? string.Empty).Contains("secret"));
        });
    }

    public void Dispose()
    {
        factory.Dispose();
    }

    private static Dictionary<string, object?> ValidFields()
    {
        return new Dictionary<string, object?>
        {
            ["kind"] = "api-failure",
            ["context"] = "nearby.load",
            ["httpStatus"] = 500,
            ["errorName"] = "HttpErrorResponse",
            ["traceId"] = clientTraceId,
            ["route"] = "/sightings/:id",
            ["sessionId"] = Guid.NewGuid()
        };
    }

    private static Dictionary<string, object?> WithField(string field, object value)
    {
        Dictionary<string, object?> fields = ValidFields();
        fields[field] = value;

        return fields;
    }

    private static string Body(string kind, Guid? sessionId = null)
    {
        Dictionary<string, object?> fields = ValidFields();
        fields["kind"] = kind;
        fields["sessionId"] = sessionId ?? Guid.NewGuid();

        return JsonSerializer.Serialize(fields);
    }

    private async Task<HttpResponseMessage> PostAsync(HttpClient client, string body, string query = "")
    {
        using StringContent content = new(body, Encoding.UTF8, "application/json");
        HttpResponseMessage response = await client.PostAsync(reportUri + query, content, cancellationToken);

        return response;
    }

    private List<FakeLogRecord> ReportRecords()
    {
        List<FakeLogRecord> records = factory.Logs.GetSnapshot()
            .Where(record => record.Category?.EndsWith("ClientErrorsController", StringComparison.Ordinal) == true)
            .ToList();

        return records;
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

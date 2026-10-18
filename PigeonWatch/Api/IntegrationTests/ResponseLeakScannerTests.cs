using System.Net;
using System.Text;

namespace PigeonWatch.IntegrationTests;

public sealed class ResponseLeakScannerTests
{
    private const string email = "Alice.Tester+pw@Example.com";
    private static readonly string jsonPlus = (char)92 + "u002B";
    private static readonly string jsonEscapedEmail = email.Replace("+", jsonPlus, StringComparison.Ordinal);
    private static readonly string jsonEscapedUpperEmail = email.ToUpperInvariant().Replace("+", jsonPlus, StringComparison.Ordinal);

    private readonly ResponseLeakScanner scanner = new();
    private readonly CancellationToken cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Scanner_reports_an_email_found_raw_in_the_body()
    {
        using HttpResponseMessage response = JsonResponse($$"""{"owner":"{{email}}"}""");

        IReadOnlyList<LeakHit> hits = await scanner.ScanAsync(response, ResponseLeakScanner.NeedlesFor(email), cancellationToken);

        Assert.Contains(hits, hit => hit.Location == "body");
    }

    [Fact]
    public async Task Scanner_reports_an_email_hidden_by_json_escaping()
    {
        string body = $$"""{"owner":"{{jsonEscapedEmail}}"}""";
        using HttpResponseMessage response = JsonResponse(body);

        IReadOnlyList<LeakHit> hits = await scanner.ScanAsync(response, ResponseLeakScanner.NeedlesFor(email), cancellationToken);

        Assert.DoesNotContain(email, body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(hits, hit => hit.Location.StartsWith("json string value", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Scanner_reports_an_email_used_as_a_json_property_name()
    {
        string body = $$"""{"{{jsonEscapedEmail}}":["x"]}""";
        using HttpResponseMessage response = JsonResponse(body);

        IReadOnlyList<LeakHit> hits = await scanner.ScanAsync(response, ResponseLeakScanner.NeedlesFor(email), cancellationToken);

        Assert.Contains(hits, hit => hit.Location.StartsWith("json property name", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Scanner_reports_an_email_nested_in_arrays_and_objects()
    {
        string body = $$$"""{"items":[{"reporter":{"contact":"{{{jsonEscapedUpperEmail}}}"}}]}""";
        using HttpResponseMessage response = JsonResponse(body);

        IReadOnlyList<LeakHit> hits = await scanner.ScanAsync(response, ResponseLeakScanner.NeedlesFor(email), cancellationToken);

        Assert.Contains(hits, hit => hit.Location.StartsWith("json string value", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Scanner_reports_an_email_in_a_header_value()
    {
        using HttpResponseMessage response = JsonResponse("{}");
        response.Headers.TryAddWithoutValidation("X-Debug-Owner", email);

        IReadOnlyList<LeakHit> hits = await scanner.ScanAsync(response, ResponseLeakScanner.NeedlesFor(email), cancellationToken);

        Assert.Contains(hits, hit => hit.Location == "header X-Debug-Owner");
    }

    [Fact]
    public async Task Scanner_reports_an_email_in_set_cookie()
    {
        using HttpResponseMessage response = JsonResponse("{}");
        response.Headers.TryAddWithoutValidation("Set-Cookie", $"owner={Uri.EscapeDataString(email)}; path=/; httponly");

        IReadOnlyList<LeakHit> hits = await scanner.ScanAsync(response, ResponseLeakScanner.NeedlesFor(email), cancellationToken);

        Assert.Contains(hits, hit => hit.Location == "header Set-Cookie");
    }

    [Fact]
    public async Task Scanner_reports_an_url_encoded_email_in_the_body()
    {
        using HttpResponseMessage response = new(HttpStatusCode.OK)
        {
            Content = new StringContent($"next=/profile?owner={Uri.EscapeDataString(email)}", Encoding.UTF8, "text/plain")
        };

        IReadOnlyList<LeakHit> hits = await scanner.ScanAsync(response, ResponseLeakScanner.NeedlesFor(email), cancellationToken);

        Assert.Contains(hits, hit => hit.Location == "body");
    }

    [Fact]
    public async Task Scanner_reports_an_email_in_a_content_header()
    {
        using HttpResponseMessage response = JsonResponse("{}");
        response.Content.Headers.TryAddWithoutValidation("Content-Disposition", $"attachment; filename=\"{email}.json\"");

        IReadOnlyList<LeakHit> hits = await scanner.ScanAsync(response, ResponseLeakScanner.NeedlesFor(email), cancellationToken);

        Assert.Contains(hits, hit => hit.Location == "header Content-Disposition");
    }

    [Fact]
    public async Task Scanner_reports_nothing_for_a_clean_response()
    {
        using HttpResponseMessage response = JsonResponse("""{"displayName":"AliceTester","items":[{"id":"1"}]}""");
        response.Headers.TryAddWithoutValidation("Set-Cookie", "session=abc123; path=/");
        response.Headers.TryAddWithoutValidation("X-Trace", "00-abc-def-01");

        IReadOnlyList<LeakHit> hits = await scanner.ScanAsync(response, ResponseLeakScanner.NeedlesFor(email), cancellationToken);

        Assert.Empty(hits);
    }

    [Fact]
    public async Task Scanner_reports_nothing_for_an_empty_response()
    {
        using HttpResponseMessage response = new(HttpStatusCode.NotFound);

        IReadOnlyList<LeakHit> hits = await scanner.ScanAsync(response, ResponseLeakScanner.NeedlesFor(email), cancellationToken);

        Assert.Empty(hits);
    }

    [Fact]
    public void Scanner_builds_raw_upper_cased_and_url_encoded_needles()
    {
        IReadOnlyList<string> needles = ResponseLeakScanner.NeedlesFor(email);

        Assert.Contains(email, needles);
        Assert.Contains(email.ToUpperInvariant(), needles, StringComparer.Ordinal);
        Assert.Contains("Alice.Tester%2Bpw%40Example.com", needles, StringComparer.OrdinalIgnoreCase);
    }

    private static HttpResponseMessage JsonResponse(string body)
    {
        HttpResponseMessage response = new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        return response;
    }
}

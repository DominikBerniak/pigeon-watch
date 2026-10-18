using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PigeonWatch.IntegrationTests;

public sealed class AccountApiClient(HttpClient client)
{
    public async Task<HttpResponseMessage> RegisterAsync(string email, string password, string displayName, CancellationToken cancellationToken = default)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/account/register",
            new { email, password, displayName },
            cancellationToken);

        return response;
    }

    public async Task<HttpResponseMessage> LoginAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/auth/login",
            new { email, password },
            cancellationToken);

        return response;
    }

    public async Task<string[]> ReadErrorCodesAsync(HttpResponseMessage response, CancellationToken cancellationToken = default)
    {
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        using JsonDocument document = JsonDocument.Parse(body);
        string[] codes = document.RootElement
            .GetProperty("errors")
            .EnumerateObject()
            .Select(property => property.Name)
            .ToArray();

        return codes;
    }

    public static async Task<string> ReadBodyWithoutTraceIdAsync(HttpResponseMessage response, CancellationToken cancellationToken = default)
    {
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        JsonObject problem = JsonNode.Parse(body)!.AsObject();
        problem.Remove("traceId");

        return problem.ToJsonString();
    }

    public static async Task<string> ReadDetailAsync(HttpResponseMessage response, CancellationToken cancellationToken = default)
    {
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        using JsonDocument document = JsonDocument.Parse(body);
        string detail = document.RootElement.GetProperty("detail").GetString()!;

        return detail;
    }
}

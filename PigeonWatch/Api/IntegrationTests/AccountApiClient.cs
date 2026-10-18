using System.Net.Http.Json;
using System.Text.Json;

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
}

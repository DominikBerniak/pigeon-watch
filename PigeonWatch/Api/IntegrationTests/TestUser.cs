using System.Net.Http.Json;
using System.Text.Json;

namespace PigeonWatch.IntegrationTests;

public sealed class TestUser(
    string email,
    string password,
    string displayName,
    Guid id,
    string accessToken,
    string refreshToken,
    HttpClient client) : IDisposable
{
    public string Email { get; } = email;

    public string Password { get; } = password;

    public string DisplayName { get; } = displayName;

    public Guid Id { get; } = id;

    public string AccessToken { get; } = accessToken;

    public string RefreshToken { get; } = refreshToken;

    public HttpClient Client { get; } = client;

    public async Task<Guid> CreateSightingAsync(
        int status = 1,
        int criticality = 2,
        double latitude = 52.2297,
        double longitude = 21.0122,
        CancellationToken cancellationToken = default)
    {
        HttpResponseMessage response = await Client.PostAsJsonAsync(
            "/sightings",
            new
            {
                name = "Test pigeon",
                description = "A pigeon with a bent wing near the fountain.",
                behaviour = "Hops instead of flying.",
                latitude,
                longitude,
                status,
                criticality
            },
            cancellationToken);

        response.EnsureSuccessStatusCode();

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        using JsonDocument document = JsonDocument.Parse(body);
        Guid sightingId = document.RootElement.GetProperty("id").GetGuid();

        return sightingId;
    }

    public void Dispose()
    {
        Client.Dispose();
    }
}

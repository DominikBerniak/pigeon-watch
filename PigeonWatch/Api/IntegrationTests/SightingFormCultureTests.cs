using System.Globalization;
using System.Net;
using System.Text.Json;

namespace PigeonWatch.IntegrationTests;

public sealed class SightingFormCultureTests : IDisposable
{
    private readonly PigeonWatchApiFactory factory = new(requestCulture: CultureInfo.GetCultureInfo("pl-PL"));
    private readonly CancellationToken cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Multipart_create_binds_dot_decimals_while_the_request_culture_is_polish()
    {
        using TestUser user = await factory.CreateUserAsync("culture.user@example.com", "Passw0rd!culture1", "CultureUser", cancellationToken);
        using MultipartFormDataContent content = SightingForm.Content(SightingForm.Fields(latitude: 52.2297, longitude: 21.0122));

        using HttpResponseMessage response = await user.Client.PostAsync("/sightings", content, cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);

        Assert.True(response.StatusCode == HttpStatusCode.Created, body);
        using JsonDocument created = JsonDocument.Parse(body);
        Assert.Equal(52.2297, created.RootElement.GetProperty("latitude").GetDouble());
        Assert.Equal(21.0122, created.RootElement.GetProperty("longitude").GetDouble());
    }

    public void Dispose()
    {
        factory.Dispose();
    }
}

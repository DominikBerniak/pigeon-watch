using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace PigeonWatch.IntegrationTests;

public sealed class SightingListContractTests : IDisposable
{
    private const string listUri = "/sightings/list";
    private const string email = "lena.lister@example.org";
    private const string displayName = "LenaLister";
    private const string password = "Passw0rd!lister1";
    private const double centreLatitude = 52.2297;
    private const double centreLongitude = 21.0122;
    private const double kilometersPerLatitudeDegree = 111.19508;

    private readonly ManualTimeProvider clock = new(DateTimeOffset.UtcNow);
    private readonly PigeonWatchApiFactory factory;
    private readonly List<TestUser> users = [];
    private readonly CancellationToken cancellationToken = TestContext.Current.CancellationToken;

    public SightingListContractTests()
    {
        factory = new PigeonWatchApiFactory(clock);
    }

    [Fact]
    public async Task List_orders_the_sightings_in_the_radius_by_urgency_then_oldest_first_across_two_pages()
    {
        TestUser user = await SeedUserAsync();
        int[] criticalities = [1, 3, 2, 3, 1, 2, 3, 2, 1, 3, 2];
        List<(Guid Id, int Criticality, int Order)> created = [];

        for (int index = 0; index < criticalities.Length; index++)
        {
            Guid id = await user.CreateSightingAsync(criticality: criticalities[index], latitude: centreLatitude, longitude: centreLongitude, cancellationToken: cancellationToken);
            created.Add((id, criticalities[index], index));
            clock.Advance(TimeSpan.FromSeconds(61));
        }

        await user.CreateSightingAsync(
            criticality: 3,
            latitude: centreLatitude + (12 / kilometersPerLatitudeDegree),
            longitude: centreLongitude,
            cancellationToken: cancellationToken);
        List<Guid> expected = created
            .OrderByDescending(sighting => sighting.Criticality)
            .ThenBy(sighting => sighting.Order)
            .Select(sighting => sighting.Id)
            .ToList();

        using HttpResponseMessage first = await user.Client.PostAsJsonAsync(listUri, new { latitude = centreLatitude, longitude = centreLongitude }, cancellationToken);
        PageView firstPage = await ReadPageAsync(first);
        using HttpResponseMessage second = await user.Client.PostAsJsonAsync(listUri, new { latitude = centreLatitude, longitude = centreLongitude, page = 2 }, cancellationToken);
        PageView secondPage = await ReadPageAsync(second);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("no-store, private", NormalizedCacheControl(first));
        Assert.Equal(expected.Take(10), firstPage.Ids);
        Assert.Equal(11, firstPage.TotalCount);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(expected.Skip(10), secondPage.Ids);
        Assert.Equal(11, secondPage.TotalCount);
    }

    [Fact]
    public async Task List_answers_a_page_past_the_end_with_empty_items_and_the_real_total_count()
    {
        TestUser user = await SeedUserAsync();
        await user.CreateSightingAsync(criticality: 2, cancellationToken: cancellationToken);
        clock.Advance(TimeSpan.FromSeconds(61));
        await user.CreateSightingAsync(criticality: 3, cancellationToken: cancellationToken);

        using HttpResponseMessage response = await user.Client.PostAsJsonAsync(
            listUri,
            new { latitude = centreLatitude, longitude = centreLongitude, page = 3, pageSize = 20 },
            cancellationToken);
        PageView page = await ReadPageAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(page.Ids);
        Assert.Equal(2, page.TotalCount);
    }

    [Fact]
    public async Task List_rejects_an_invalid_page_page_size_and_status_with_400_keyed_by_code()
    {
        TestUser user = await SeedUserAsync();

        using HttpResponseMessage response = await user.Client.PostAsJsonAsync(
            listUri,
            new { latitude = centreLatitude, longitude = centreLongitude, statuses = new[] { 42 }, page = 0, pageSize = 15 },
            cancellationToken);
        string[] errorCodes = await new AccountApiClient(user.Client).ReadErrorCodesAsync(response, cancellationToken);
        using HttpResponseMessage resolved = await user.Client.PostAsJsonAsync(
            listUri,
            new { latitude = centreLatitude, longitude = centreLongitude, statuses = new[] { 6 } },
            cancellationToken);
        using HttpResponseMessage missingCoordinates = await user.Client.PostAsJsonAsync(listUri, new { page = 1 }, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["InvalidFilterStatus", "InvalidPage", "InvalidPageSize"], errorCodes.Order(StringComparer.Ordinal));
        Assert.Equal(HttpStatusCode.OK, resolved.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, missingCoordinates.StatusCode);
    }

    [Fact]
    public async Task List_answers_unauthorized_to_an_anonymous_caller()
    {
        using HttpRequestMessage request = new(HttpMethod.Post, listUri)
        {
            Content = JsonContent.Create(new { latitude = centreLatitude, longitude = centreLongitude })
        };

        using HttpResponseMessage response = await factory.SendFromFreshAddressAsync(request, null, cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(body);
    }

    public void Dispose()
    {
        foreach (TestUser user in users)
            user.Dispose();

        factory.Dispose();
    }

    private static string NormalizedCacheControl(HttpResponseMessage response)
    {
        return string.Join(", ", response.Headers.CacheControl!.ToString().Split(',').Select(part => part.Trim()).Order(StringComparer.Ordinal));
    }

    private async Task<TestUser> SeedUserAsync()
    {
        TestUser user = await factory.CreateUserAsync(email, password, displayName, cancellationToken);
        users.Add(user);

        return user;
    }

    private async Task<PageView> ReadPageAsync(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        using JsonDocument document = JsonDocument.Parse(body);
        List<Guid> ids = document.RootElement
            .GetProperty("items")
            .EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid())
            .ToList();
        int totalCount = document.RootElement.GetProperty("totalCount").GetInt32();

        return new PageView(ids, totalCount);
    }

    private sealed record PageView(IReadOnlyList<Guid> Ids, int TotalCount);
}

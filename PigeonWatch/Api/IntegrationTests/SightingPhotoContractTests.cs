using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace PigeonWatch.IntegrationTests;

public sealed class SightingPhotoContractTests : IDisposable
{
    private const string aliceEmail = "alice.photo@example.com";
    private const string aliceDisplayName = "AlicePhoto";
    private const string bobEmail = "bob.photo@example.net";
    private const string bobDisplayName = "BobPhoto";
    private const string password = "Passw0rd!photo1";
    private const string nearbyUri = "/sightings/nearby?latitude=52.2297&longitude=21.0122";

    private readonly ManualTimeProvider clock = new(DateTimeOffset.UtcNow);
    private readonly PigeonWatchApiFactory factory;
    private readonly List<TestUser> users = [];
    private readonly CancellationToken cancellationToken = TestContext.Current.CancellationToken;

    public SightingPhotoContractTests()
    {
        factory = new PigeonWatchApiFactory(clock);
    }

    [Fact]
    public async Task Multipart_create_without_a_photo_returns_201_with_has_photo_false_and_stores_nothing()
    {
        TestUser alice = await SeedUserAsync(aliceEmail, aliceDisplayName);

        using HttpResponseMessage response = await PostAsync(alice, null);
        using JsonDocument created = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.False(created.RootElement.GetProperty("hasPhoto").GetBoolean());
        Assert.Empty(factory.PhotoStore.BlobNames);
    }

    [Fact]
    public async Task Multipart_create_with_an_empty_photo_part_is_treated_as_no_photo()
    {
        TestUser alice = await SeedUserAsync(aliceEmail, aliceDisplayName);

        using HttpResponseMessage response = await PostAsync(alice, []);
        using JsonDocument created = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.False(created.RootElement.GetProperty("hasPhoto").GetBoolean());
        Assert.Empty(factory.PhotoStore.BlobNames);
    }

    [Fact]
    public async Task Multipart_create_with_a_photo_returns_201_with_has_photo_true_and_the_photo_streams_back_as_a_jpeg_without_exif()
    {
        TestUser alice = await SeedUserAsync(aliceEmail, aliceDisplayName);
        TestUser bob = await SeedUserAsync(bobEmail, bobDisplayName);
        byte[] upload = TestPhotos.JpegWithExif();
        Assert.True(TestPhotos.ContainsExifSegment(upload));

        using HttpResponseMessage response = await PostAsync(alice, upload);
        using JsonDocument created = await ReadJsonAsync(response);
        Guid sightingId = created.RootElement.GetProperty("id").GetGuid();

        using HttpResponseMessage photo = await bob.Client.GetAsync($"/sightings/{sightingId}/photo", cancellationToken);
        byte[] downloaded = await photo.Content.ReadAsByteArrayAsync(cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(created.RootElement.GetProperty("hasPhoto").GetBoolean());
        Assert.False(created.RootElement.TryGetProperty("photoBlobName", out JsonElement _));
        Assert.Equal(HttpStatusCode.OK, photo.StatusCode);
        Assert.Equal("image/jpeg", photo.Content.Headers.ContentType?.MediaType);
        Assert.True(photo.Headers.CacheControl is { Private: true, NoStore: true });
        Assert.True(TestPhotos.IsJpeg(downloaded));
        Assert.False(TestPhotos.ContainsExifSegment(downloaded));
        Assert.Equal([factory.ReadPhotoBlobName(sightingId)], factory.PhotoStore.BlobNames);
    }

    [Fact]
    public async Task Anonymous_photo_request_is_unauthorized()
    {
        TestUser alice = await SeedUserAsync(aliceEmail, aliceDisplayName);
        Guid sightingId = await alice.CreateSightingAsync(photo: TestPhotos.JpegWithExif(), cancellationToken: cancellationToken);
        using HttpRequestMessage request = new(HttpMethod.Get, $"/sightings/{sightingId}/photo");

        using HttpResponseMessage response = await factory.SendFromFreshAddressAsync(request, null, cancellationToken);
        byte[] body = await response.Content.ReadAsByteArrayAsync(cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(body);
    }

    [Fact]
    public async Task Photo_of_an_unknown_sighting_is_404_and_logs_the_id_at_information()
    {
        TestUser alice = await SeedUserAsync(aliceEmail, aliceDisplayName);
        Guid unknownId = Guid.NewGuid();
        factory.ClearLogs();

        using HttpResponseMessage response = await alice.Client.GetAsync($"/sightings/{unknownId}/photo", cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(404, await ReadProblemStatusAsync(response));
        FakeLogRecord information = Assert.Single(
            factory.Logs.GetSnapshot(),
            record => record.StructuredState?.Any(pair => pair.Key == "SightingId") == true);
        Assert.Equal(LogLevel.Information, information.Level);
        Assert.Equal(unknownId.ToString(), State(information, "SightingId"));
    }

    [Fact]
    public async Task Photo_of_a_sighting_without_a_photo_is_404()
    {
        TestUser alice = await SeedUserAsync(aliceEmail, aliceDisplayName);
        Guid sightingId = await alice.CreateSightingAsync(cancellationToken: cancellationToken);

        using HttpResponseMessage response = await alice.Client.GetAsync($"/sightings/{sightingId}/photo", cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(404, await ReadProblemStatusAsync(response));
    }

    [Fact]
    public async Task Photo_whose_blob_is_missing_is_404_and_logs_a_warning_with_the_blob_name()
    {
        TestUser alice = await SeedUserAsync(aliceEmail, aliceDisplayName);
        Guid sightingId = await alice.CreateSightingAsync(photo: TestPhotos.JpegWithExif(), cancellationToken: cancellationToken);
        string blobName = factory.ReadPhotoBlobName(sightingId)!;
        factory.PhotoStore.Remove(blobName);
        factory.ClearLogs();

        using HttpResponseMessage response = await alice.Client.GetAsync($"/sightings/{sightingId}/photo", cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        FakeLogRecord warning = Assert.Single(factory.Logs.GetSnapshot(), record => record.Level == LogLevel.Warning);
        Assert.Equal(blobName, State(warning, "BlobName"));
        Assert.Equal(sightingId.ToString(), State(warning, "SightingId"));
    }

    [Fact]
    public async Task Photo_above_the_size_limit_is_400_photo_too_large_and_logged_at_warning()
    {
        TestUser alice = await SeedUserAsync(aliceEmail, aliceDisplayName);
        byte[] upload = new byte[2 * 1024 * 1024 + 1];
        factory.ClearLogs();

        using HttpResponseMessage response = await PostAsync(alice, upload);
        string[] codes = await new AccountApiClient(alice.Client).ReadErrorCodesAsync(response, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["PhotoTooLarge"], codes);
        Assert.Contains(factory.Logs.GetSnapshot(), record => record.Level == LogLevel.Warning && State(record, "ErrorCode") == "PhotoTooLarge");
        Assert.Equal(0, factory.CountSightings());
        Assert.Empty(factory.PhotoStore.BlobNames);
    }

    [Fact]
    public async Task Gif_photo_is_400_photo_unsupported_type()
    {
        TestUser alice = await SeedUserAsync(aliceEmail, aliceDisplayName);

        using HttpResponseMessage response = await PostAsync(alice, TestPhotos.Gif, "image/gif");
        string[] codes = await new AccountApiClient(alice.Client).ReadErrorCodesAsync(response, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["PhotoUnsupportedType"], codes);
        Assert.Equal(0, factory.CountSightings());
        Assert.Empty(factory.PhotoStore.BlobNames);
    }

    [Fact]
    public async Task Body_above_the_request_limit_is_400_photo_too_large_and_logged_at_warning()
    {
        TestUser alice = await SeedUserAsync(aliceEmail, aliceDisplayName);
        byte[] upload = new byte[2 * 1024 * 1024 + 600 * 1024];
        factory.ClearLogs();

        using HttpResponseMessage response = await PostAsync(alice, upload);
        string[] codes = await new AccountApiClient(alice.Client).ReadErrorCodesAsync(response, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["PhotoTooLarge"], codes);
        FakeLogRecord warning = Assert.Single(factory.Logs.GetSnapshot(), record => record.Level == LogLevel.Warning);
        Assert.Equal("PhotoTooLarge", State(warning, "ErrorCode"));
        Assert.Equal("sightings", State(warning, "Route"));
        Assert.Equal(0, factory.CountSightings());
        Assert.Empty(factory.PhotoStore.BlobNames);
    }

    [Fact]
    public async Task Failing_photo_store_is_503_photo_storage_unavailable_with_retry_after_saves_no_row_and_logs_an_error()
    {
        TestUser alice = await SeedUserAsync(aliceEmail, aliceDisplayName);
        factory.PhotoStore.IsUnavailable = true;
        factory.ClearLogs();

        using HttpResponseMessage response = await PostAsync(alice, TestPhotos.JpegWithExif());
        string[] codes = await new AccountApiClient(alice.Client).ReadErrorCodesAsync(response, cancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(["PhotoStorageUnavailable"], codes);
        Assert.Equal("10", response.Headers.RetryAfter?.ToString());
        Assert.Equal(0, factory.CountSightings());
        FakeLogRecord error = Assert.Single(factory.Logs.GetSnapshot(), record => record.Level == LogLevel.Error);
        Assert.NotNull(error.Exception);
    }

    [Fact]
    public async Task List_nearby_mine_and_get_responses_carry_has_photo()
    {
        TestUser alice = await SeedUserAsync(aliceEmail, aliceDisplayName);
        Guid withPhoto = await alice.CreateSightingAsync(photo: TestPhotos.JpegWithExif(), cancellationToken: cancellationToken);
        clock.Advance(TimeSpan.FromMinutes(2));
        Guid withoutPhoto = await alice.CreateSightingAsync(cancellationToken: cancellationToken);
        Dictionary<Guid, bool> expected = new() { [withPhoto] = true, [withoutPhoto] = false };

        using HttpResponseMessage list = await alice.Client.PostAsJsonAsync("/sightings/list", new { latitude = 52.2297, longitude = 21.0122 }, cancellationToken);
        using HttpResponseMessage nearby = await alice.Client.GetAsync(nearbyUri, cancellationToken);
        using HttpResponseMessage mine = await alice.Client.GetAsync("/sightings/mine", cancellationToken);
        using HttpResponseMessage single = await alice.Client.GetAsync($"/sightings/{withPhoto}", cancellationToken);
        using JsonDocument singleBody = await ReadJsonAsync(single);

        Assert.Equal(expected, await ReadHasPhotoAsync(list));
        Assert.Equal(expected, await ReadHasPhotoAsync(nearby));
        Assert.Equal(expected, await ReadHasPhotoAsync(mine));
        Assert.True(singleBody.RootElement.GetProperty("hasPhoto").GetBoolean());
    }

    [Fact]
    public async Task Client_configuration_serves_the_photo_limits()
    {
        using HttpClient client = factory.CreateApiClient();

        using HttpResponseMessage response = await client.GetAsync("/configuration/client", cancellationToken);
        using JsonDocument configuration = await ReadJsonAsync(response);
        JsonElement rules = configuration.RootElement.GetProperty("sightingRules");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2 * 1024 * 1024, rules.GetProperty("photoMaxBytes").GetInt32());
        Assert.Equal(1600, rules.GetProperty("photoMaxDimension").GetInt32());
        Assert.Equal(
            ["image/jpeg", "image/png", "image/webp"],
            rules.GetProperty("photoAllowedContentTypes").EnumerateArray().Select(item => item.GetString()!).ToArray());
    }

    public void Dispose()
    {
        foreach (TestUser user in users)
            user.Dispose();

        factory.Dispose();
    }

    private async Task<TestUser> SeedUserAsync(string email, string displayName)
    {
        TestUser user = await factory.CreateUserAsync(email, password, displayName, cancellationToken);
        users.Add(user);

        return user;
    }

    private async Task<HttpResponseMessage> PostAsync(TestUser user, byte[]? photo, string photoContentType = SightingForm.JpegContentType)
    {
        using MultipartFormDataContent content = SightingForm.Content(SightingForm.Fields(), photo, photoContentType);
        HttpResponseMessage response = await user.Client.PostAsync("/sightings", content, cancellationToken);

        return response;
    }

    private async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        JsonDocument document = JsonDocument.Parse(body);

        return document;
    }

    private async Task<int> ReadProblemStatusAsync(HttpResponseMessage response)
    {
        using JsonDocument problem = await ReadJsonAsync(response);
        int status = problem.RootElement.GetProperty("status").GetInt32();

        return status;
    }

    private async Task<Dictionary<Guid, bool>> ReadHasPhotoAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using JsonDocument document = await ReadJsonAsync(response);
        Dictionary<Guid, bool> hasPhoto = document.RootElement
            .GetProperty("items")
            .EnumerateArray()
            .ToDictionary(item => item.GetProperty("id").GetGuid(), item => item.GetProperty("hasPhoto").GetBoolean());

        return hasPhoto;
    }

    private static string? State(FakeLogRecord record, string key)
    {
        string? value = record.StructuredState?.FirstOrDefault(pair => pair.Key == key).Value;

        return value;
    }
}

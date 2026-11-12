using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace PigeonWatch.IntegrationTests;

public sealed class SightingEditContractTests : IDisposable
{
    private const string aliceEmail = "alice.edit@example.org";
    private const string aliceDisplayName = "AliceEdit";
    private const string bobEmail = "bob.edit@example.net";
    private const string bobDisplayName = "BobEdit";
    private const string carolEmail = "carol.edit@example.com";
    private const string carolDisplayName = "CarolEdit";
    private const string password = "Passw0rd!editor1";
    private const double latitude = 52.2297;
    private const double longitude = 21.0122;

    private readonly ManualTimeProvider clock = new(DateTimeOffset.UtcNow);
    private readonly PigeonWatchApiFactory factory;
    private readonly List<TestUser> users = [];
    private readonly CancellationToken cancellationToken = TestContext.Current.CancellationToken;

    public SightingEditContractTests()
    {
        factory = new PigeonWatchApiFactory(clock);
    }

    [Fact]
    public async Task Alices_create_response_carries_her_edit_permissions()
    {
        TestUser alice = await SeedUserAsync(aliceEmail, aliceDisplayName);
        using MultipartFormDataContent content = SightingForm.Content(SightingForm.Fields(1, 2, latitude, longitude));

        using HttpResponseMessage created = await alice.Client.PostAsync("/sightings", content, cancellationToken);
        JsonElement? permissions = await ReadEditPermissionsAsync(created);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.NotNull(permissions);
        Assert.True(permissions.Value.GetProperty("canEditDescription").GetBoolean());
        Assert.Equal([2, 9], AllowedNextStatuses(permissions.Value));
    }

    [Fact]
    public async Task Alices_get_carries_editable_description_and_the_transitions_from_spotted()
    {
        World world = await SeedAsync();

        JsonElement? permissions = await GetEditPermissionsAsync(world.Alice, world.SightingId);

        Assert.NotNull(permissions);
        Assert.True(permissions.Value.GetProperty("canEditDescription").GetBoolean());
        Assert.Equal([2, 9], AllowedNextStatuses(permissions.Value));
    }

    [Fact]
    public async Task Alices_description_locks_after_bob_comments_and_unlocks_when_he_deletes_the_comment()
    {
        World world = await SeedAsync();

        Guid commentId = await world.Bob.CreateCommentAsync(world.SightingId, "I saw it too", cancellationToken);
        JsonElement? afterComment = await GetEditPermissionsAsync(world.Alice, world.SightingId);
        using HttpResponseMessage delete = await world.Bob.Client.DeleteAsync($"/sightings/{world.SightingId}/comments/{commentId}", cancellationToken);
        JsonElement? afterDelete = await GetEditPermissionsAsync(world.Alice, world.SightingId);

        Assert.False(afterComment!.Value.GetProperty("canEditDescription").GetBoolean());
        Assert.Equal([2, 9], AllowedNextStatuses(afterComment.Value));
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
        Assert.True(afterDelete!.Value.GetProperty("canEditDescription").GetBoolean());
    }

    [Fact]
    public async Task Alices_own_comment_keeps_her_description_editable()
    {
        World world = await SeedAsync();

        await world.Alice.CreateCommentAsync(world.SightingId, "Still here", cancellationToken);
        JsonElement? permissions = await GetEditPermissionsAsync(world.Alice, world.SightingId);

        Assert.True(permissions!.Value.GetProperty("canEditDescription").GetBoolean());
    }

    [Fact]
    public async Task Alices_description_locks_after_bob_starts_a_thread()
    {
        World world = await SeedAsync();

        await world.Bob.StartThreadAsync(world.SightingId, "I can help catch it", cancellationToken);
        JsonElement? permissions = await GetEditPermissionsAsync(world.Alice, world.SightingId);

        Assert.False(permissions!.Value.GetProperty("canEditDescription").GetBoolean());
    }

    [Fact]
    public async Task Bobs_and_carols_gets_carry_null_edit_permissions()
    {
        World world = await SeedAsync();
        await world.Bob.CreateCommentAsync(world.SightingId, "I saw it too", cancellationToken);

        using HttpResponseMessage bobGet = await world.Bob.Client.GetAsync($"/sightings/{world.SightingId}", cancellationToken);
        using HttpResponseMessage carolGet = await world.Carol.Client.GetAsync($"/sightings/{world.SightingId}", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, bobGet.StatusCode);
        Assert.Equal(HttpStatusCode.OK, carolGet.StatusCode);
        Assert.Null(await ReadEditPermissionsAsync(bobGet));
        Assert.Null(await ReadEditPermissionsAsync(carolGet));
    }

    [Fact]
    public async Task Nearby_list_and_mine_items_carry_null_edit_permissions_even_for_the_owner()
    {
        World world = await SeedAsync();

        using HttpResponseMessage nearby = await world.Alice.Client.GetAsync("/sightings/nearby?latitude=52.2297&longitude=21.0122", cancellationToken);
        using HttpResponseMessage list = await world.Alice.Client.PostAsJsonAsync("/sightings/list", new { latitude, longitude }, cancellationToken);
        using HttpResponseMessage mine = await world.Alice.Client.GetAsync("/sightings/mine", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, nearby.StatusCode);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(HttpStatusCode.OK, mine.StatusCode);
        await AssertItemsHaveNullEditPermissionsAsync(nearby, world.SightingId);
        await AssertItemsHaveNullEditPermissionsAsync(list, world.SightingId);
        await AssertItemsHaveNullEditPermissionsAsync(mine, world.SightingId);
    }

    [Fact]
    public async Task Alice_edits_name_behaviour_criticality_and_description_and_the_location_stays()
    {
        World world = await SeedAsync();

        using HttpResponseMessage update = await world.Alice.UpdateSightingAsync(
            world.SightingId,
            SightingForm.UpdateFields(criticality: 3, name: "Renamed pigeon", description: "  Now limping on both legs.  ", behaviour: "Sits still."),
            cancellationToken: cancellationToken);
        using JsonDocument updated = await ReadJsonAsync(update);
        SightingView readBack = await ReadSightingAsync(world.Alice, world.SightingId);

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal(world.SightingId, updated.RootElement.GetProperty("id").GetGuid());
        Assert.True(updated.RootElement.GetProperty("isMine").GetBoolean());
        Assert.True(updated.RootElement.GetProperty("editPermissions").GetProperty("canEditDescription").GetBoolean());
        Assert.Equal([2, 9], AllowedNextStatuses(updated.RootElement.GetProperty("editPermissions")));
        Assert.Equal(new SightingView("Renamed pigeon", "Now limping on both legs.", "Sits still.", 1, 3, latitude, longitude, false), readBack);
    }

    [Fact]
    public async Task Forged_latitude_and_longitude_parts_are_ignored()
    {
        World world = await SeedAsync();
        Dictionary<string, string> fields = SightingForm.UpdateFields(name: "Moved pigeon");
        fields["latitude"] = "10.5";
        fields["longitude"] = "-20.25";

        using HttpResponseMessage update = await world.Alice.UpdateSightingAsync(world.SightingId, fields, cancellationToken: cancellationToken);
        using JsonDocument updated = await ReadJsonAsync(update);
        SightingView readBack = await ReadSightingAsync(world.Alice, world.SightingId);

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal(latitude, updated.RootElement.GetProperty("latitude").GetDouble());
        Assert.Equal(longitude, updated.RootElement.GetProperty("longitude").GetDouble());
        Assert.Equal("Moved pigeon", readBack.Name);
        Assert.Equal(latitude, readBack.Latitude);
        Assert.Equal(longitude, readBack.Longitude);
    }

    [Fact]
    public async Task Spotted_to_caught_is_accepted_and_the_permissions_follow_the_new_status()
    {
        World world = await SeedAsync();

        using HttpResponseMessage update = await world.Alice.UpdateSightingAsync(world.SightingId, SightingForm.UpdateFields(status: 2), cancellationToken: cancellationToken);
        using JsonDocument updated = await ReadJsonAsync(update);
        SightingView readBack = await ReadSightingAsync(world.Alice, world.SightingId);

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal(2, updated.RootElement.GetProperty("status").GetInt32());
        Assert.Equal([3, 4, 7], AllowedNextStatuses(updated.RootElement.GetProperty("editPermissions")));
        Assert.Equal(2, readBack.Status);
    }

    [Fact]
    public async Task Spotted_to_at_the_vet_is_400_invalid_status_transition_and_writes_nothing()
    {
        World world = await SeedAsync();
        SightingView before = await ReadSightingAsync(world.Alice, world.SightingId);

        using HttpResponseMessage update = await world.Alice.UpdateSightingAsync(
            world.SightingId,
            SightingForm.UpdateFields(status: 3, name: "Should not be saved"),
            cancellationToken: cancellationToken);
        string[] codes = await ReadErrorCodesAsync(world.Alice, update);
        SightingView after = await ReadSightingAsync(world.Alice, world.SightingId);

        Assert.Equal(HttpStatusCode.BadRequest, update.StatusCode);
        Assert.Equal(["InvalidStatusTransition"], codes);
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task An_end_state_accepts_no_further_status()
    {
        World world = await SeedAsync();
        using HttpResponseMessage resolve = await world.Alice.UpdateSightingAsync(world.SightingId, SightingForm.UpdateFields(status: 9), cancellationToken: cancellationToken);
        using JsonDocument resolved = await ReadJsonAsync(resolve);

        using HttpResponseMessage reopen = await world.Alice.UpdateSightingAsync(world.SightingId, SightingForm.UpdateFields(status: 1), cancellationToken: cancellationToken);
        string[] reopenCodes = await ReadErrorCodesAsync(world.Alice, reopen);
        using HttpResponseMessage caught = await world.Alice.UpdateSightingAsync(world.SightingId, SightingForm.UpdateFields(status: 2), cancellationToken: cancellationToken);
        string[] caughtCodes = await ReadErrorCodesAsync(world.Alice, caught);
        SightingView readBack = await ReadSightingAsync(world.Alice, world.SightingId);

        Assert.Equal(HttpStatusCode.OK, resolve.StatusCode);
        Assert.Empty(AllowedNextStatuses(resolved.RootElement.GetProperty("editPermissions")));
        Assert.Equal(HttpStatusCode.BadRequest, reopen.StatusCode);
        Assert.Equal(["InvalidStatusTransition"], reopenCodes);
        Assert.Equal(HttpStatusCode.BadRequest, caught.StatusCode);
        Assert.Equal(["InvalidStatusTransition"], caughtCodes);
        Assert.Equal(9, readBack.Status);
    }

    [Fact]
    public async Task The_same_status_is_accepted()
    {
        World world = await SeedAsync();

        using HttpResponseMessage update = await world.Alice.UpdateSightingAsync(
            world.SightingId,
            SightingForm.UpdateFields(status: 1, criticality: 1),
            cancellationToken: cancellationToken);
        SightingView readBack = await ReadSightingAsync(world.Alice, world.SightingId);

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal(1, readBack.Status);
        Assert.Equal(1, readBack.Criticality);
    }

    [Fact]
    public async Task After_bobs_comment_a_changed_description_is_400_description_locked_and_writes_nothing()
    {
        World world = await SeedAsync();
        await world.Bob.CreateCommentAsync(world.SightingId, "I saw it too", cancellationToken);
        SightingView before = await ReadSightingAsync(world.Alice, world.SightingId);
        factory.ClearLogs();

        using HttpResponseMessage update = await world.Alice.UpdateSightingAsync(
            world.SightingId,
            SightingForm.UpdateFields(status: 2, description: "A different description."),
            cancellationToken: cancellationToken);
        string[] codes = await ReadErrorCodesAsync(world.Alice, update);
        SightingView after = await ReadSightingAsync(world.Alice, world.SightingId);

        Assert.Equal(HttpStatusCode.BadRequest, update.StatusCode);
        Assert.Equal(["DescriptionLocked"], codes);
        Assert.Equal(before, after);
        Assert.DoesNotContain(factory.Logs.GetSnapshot(), record => record.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task After_bobs_comment_the_unchanged_description_with_status_caught_is_accepted()
    {
        World world = await SeedAsync();
        await world.Bob.CreateCommentAsync(world.SightingId, "I saw it too", cancellationToken);

        using HttpResponseMessage update = await world.Alice.UpdateSightingAsync(world.SightingId, SightingForm.UpdateFields(status: 2), cancellationToken: cancellationToken);
        using JsonDocument updated = await ReadJsonAsync(update);
        SightingView readBack = await ReadSightingAsync(world.Alice, world.SightingId);

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.False(updated.RootElement.GetProperty("editPermissions").GetProperty("canEditDescription").GetBoolean());
        Assert.Equal(2, readBack.Status);
    }

    [Fact]
    public async Task After_bob_deletes_his_comment_a_changed_description_is_accepted()
    {
        World world = await SeedAsync();
        Guid commentId = await world.Bob.CreateCommentAsync(world.SightingId, "I saw it too", cancellationToken);
        using HttpResponseMessage delete = await world.Bob.Client.DeleteAsync($"/sightings/{world.SightingId}/comments/{commentId}", cancellationToken);

        using HttpResponseMessage update = await world.Alice.UpdateSightingAsync(
            world.SightingId,
            SightingForm.UpdateFields(description: "A different description."),
            cancellationToken: cancellationToken);
        SightingView readBack = await ReadSightingAsync(world.Alice, world.SightingId);

        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal("A different description.", readBack.Description);
    }

    [Fact]
    public async Task After_a_thread_exists_a_changed_description_is_400_description_locked()
    {
        World world = await SeedAsync();
        await world.Bob.StartThreadAsync(world.SightingId, "I can help catch it", cancellationToken);

        using HttpResponseMessage update = await world.Alice.UpdateSightingAsync(
            world.SightingId,
            SightingForm.UpdateFields(description: "A different description."),
            cancellationToken: cancellationToken);
        string[] codes = await ReadErrorCodesAsync(world.Alice, update);

        Assert.Equal(HttpStatusCode.BadRequest, update.StatusCode);
        Assert.Equal(["DescriptionLocked"], codes);
    }

    [Fact]
    public async Task Bob_and_carol_get_403_and_alices_sighting_is_unchanged()
    {
        World world = await SeedAsync();
        SightingView before = await ReadSightingAsync(world.Alice, world.SightingId);
        Dictionary<string, string> fields = SightingForm.UpdateFields(status: 2, criticality: 3, name: "Hijacked", description: "Hijacked description.");

        using HttpResponseMessage bobUpdate = await world.Bob.UpdateSightingAsync(world.SightingId, fields, cancellationToken: cancellationToken);
        using HttpResponseMessage carolUpdate = await world.Carol.UpdateSightingAsync(world.SightingId, fields, TestPhotos.JpegWithExif(), cancellationToken);
        string bobBody = await bobUpdate.Content.ReadAsStringAsync(cancellationToken);
        SightingView after = await ReadSightingAsync(world.Alice, world.SightingId);

        Assert.Equal(HttpStatusCode.Forbidden, bobUpdate.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, carolUpdate.StatusCode);
        AssertCarriesNoPrivateData(bobBody, world);
        Assert.Equal(before, after);
        Assert.Empty(factory.PhotoStore.SavedBlobNames);
    }

    [Fact]
    public async Task An_unknown_sighting_is_404()
    {
        World world = await SeedAsync();

        using HttpResponseMessage update = await world.Alice.UpdateSightingAsync(Guid.NewGuid(), SightingForm.UpdateFields(), cancellationToken: cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);
    }

    [Fact]
    public async Task A_new_photo_replaces_the_stored_one_and_deletes_the_old_blobs()
    {
        World world = await SeedAsync(TestPhotos.JpegWithExif());
        string oldBlobName = factory.ReadPhotoBlobName(world.SightingId)!;

        using HttpResponseMessage update = await world.Alice.UpdateSightingAsync(
            world.SightingId,
            SightingForm.UpdateFields(),
            TestPhotos.JpegWithExif(),
            cancellationToken);
        using JsonDocument updated = await ReadJsonAsync(update);
        string newBlobName = factory.ReadPhotoBlobName(world.SightingId)!;
        using HttpResponseMessage photo = await world.Bob.Client.GetAsync($"/sightings/{world.SightingId}/photo", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.True(updated.RootElement.GetProperty("hasPhoto").GetBoolean());
        Assert.NotEqual(oldBlobName, newBlobName);
        Assert.Equal(
            new HashSet<string> { newBlobName, ThumbnailNameOf(newBlobName) },
            factory.PhotoStore.BlobNames.ToHashSet());
        Assert.Equal(HttpStatusCode.OK, photo.StatusCode);
    }

    [Fact]
    public async Task A_new_photo_is_added_to_a_sighting_without_one()
    {
        World world = await SeedAsync();

        using HttpResponseMessage update = await world.Alice.UpdateSightingAsync(
            world.SightingId,
            SightingForm.UpdateFields(),
            TestPhotos.JpegWithExif(),
            cancellationToken);
        string blobName = factory.ReadPhotoBlobName(world.SightingId)!;

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.True((await ReadSightingAsync(world.Alice, world.SightingId)).HasPhoto);
        Assert.Equal(
            new HashSet<string> { blobName, ThumbnailNameOf(blobName) },
            factory.PhotoStore.BlobNames.ToHashSet());
    }

    [Fact]
    public async Task Remove_photo_clears_has_photo_and_deletes_the_blobs()
    {
        World world = await SeedAsync(TestPhotos.JpegWithExif());

        using HttpResponseMessage update = await world.Alice.UpdateSightingAsync(
            world.SightingId,
            SightingForm.UpdateFields(removePhoto: true),
            cancellationToken: cancellationToken);
        using JsonDocument updated = await ReadJsonAsync(update);
        using HttpResponseMessage photo = await world.Alice.Client.GetAsync($"/sightings/{world.SightingId}/photo", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.False(updated.RootElement.GetProperty("hasPhoto").GetBoolean());
        Assert.Null(factory.ReadPhotoBlobName(world.SightingId));
        Assert.Empty(factory.PhotoStore.BlobNames);
        Assert.Equal(HttpStatusCode.NotFound, photo.StatusCode);
    }

    [Fact]
    public async Task A_photo_together_with_remove_photo_is_400_photo_conflict_and_keeps_the_stored_photo()
    {
        World world = await SeedAsync(TestPhotos.JpegWithExif());
        string blobName = factory.ReadPhotoBlobName(world.SightingId)!;

        using HttpResponseMessage update = await world.Alice.UpdateSightingAsync(
            world.SightingId,
            SightingForm.UpdateFields(removePhoto: true),
            TestPhotos.JpegWithExif(),
            cancellationToken);
        string[] codes = await ReadErrorCodesAsync(world.Alice, update);

        Assert.Equal(HttpStatusCode.BadRequest, update.StatusCode);
        Assert.Equal(["PhotoConflict"], codes);
        Assert.Equal(blobName, factory.ReadPhotoBlobName(world.SightingId));
        Assert.Equal(
            new HashSet<string> { blobName, ThumbnailNameOf(blobName) },
            factory.PhotoStore.BlobNames.ToHashSet());
    }

    [Fact]
    public async Task An_undecodable_photo_is_400_photo_unreadable_and_keeps_the_stored_photo()
    {
        World world = await SeedAsync(TestPhotos.JpegWithExif());
        string blobName = factory.ReadPhotoBlobName(world.SightingId)!;
        SightingView before = await ReadSightingAsync(world.Alice, world.SightingId);

        using HttpResponseMessage update = await world.Alice.UpdateSightingAsync(
            world.SightingId,
            SightingForm.UpdateFields(name: "Should not be saved"),
            TestPhotos.TruncatedJpeg(),
            cancellationToken);
        string[] codes = await ReadErrorCodesAsync(world.Alice, update);

        Assert.Equal(HttpStatusCode.BadRequest, update.StatusCode);
        Assert.Equal(["PhotoUnreadable"], codes);
        Assert.Equal(blobName, factory.ReadPhotoBlobName(world.SightingId));
        Assert.Equal(
            new HashSet<string> { blobName, ThumbnailNameOf(blobName) },
            factory.PhotoStore.BlobNames.ToHashSet());
        Assert.Equal(before, await ReadSightingAsync(world.Alice, world.SightingId));
    }

    [Fact]
    public async Task An_unavailable_photo_store_is_503_and_leaves_the_row_unchanged()
    {
        World world = await SeedAsync(TestPhotos.JpegWithExif());
        string blobName = factory.ReadPhotoBlobName(world.SightingId)!;
        SightingView before = await ReadSightingAsync(world.Alice, world.SightingId);
        factory.PhotoStore.IsUnavailable = true;

        using HttpResponseMessage update = await world.Alice.UpdateSightingAsync(
            world.SightingId,
            SightingForm.UpdateFields(name: "Should not be saved"),
            TestPhotos.JpegWithExif(),
            cancellationToken);
        string[] codes = await ReadErrorCodesAsync(world.Alice, update);
        factory.PhotoStore.IsUnavailable = false;

        Assert.Equal(HttpStatusCode.ServiceUnavailable, update.StatusCode);
        Assert.Equal(["PhotoStorageUnavailable"], codes);
        Assert.Equal(blobName, factory.ReadPhotoBlobName(world.SightingId));
        Assert.Equal(before, await ReadSightingAsync(world.Alice, world.SightingId));
    }

    [Fact]
    public async Task Update_responses_carry_no_reporter_id_audit_fields_or_email()
    {
        World world = await SeedAsync();

        using HttpResponseMessage update = await world.Alice.UpdateSightingAsync(world.SightingId, SightingForm.UpdateFields(status: 2), cancellationToken: cancellationToken);
        string body = await update.Content.ReadAsStringAsync(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        AssertCarriesNoPrivateData(body, world);
    }

    public void Dispose()
    {
        foreach (TestUser user in users)
            user.Dispose();

        factory.Dispose();
    }

    private static void AssertCarriesNoPrivateData(string body, World world)
    {
        string[] forbidden =
        [
            "reporterId",
            "createUser",
            "updateUser",
            "createDate",
            "updateDate",
            "photoBlobName",
            world.Alice.Id.ToString(),
            world.Bob.Id.ToString(),
            world.Carol.Id.ToString(),
            world.Alice.Email,
            world.Bob.Email,
            world.Carol.Email
        ];

        foreach (string needle in forbidden)
            Assert.DoesNotContain(needle, body, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<SightingView> ReadSightingAsync(TestUser user, Guid sightingId)
    {
        using HttpResponseMessage response = await user.Client.GetAsync($"/sightings/{sightingId}", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using JsonDocument document = await ReadJsonAsync(response);
        JsonElement root = document.RootElement;
        SightingView view = new(
            root.GetProperty("name").GetString(),
            root.GetProperty("description").GetString()!,
            root.GetProperty("behaviour").GetString()!,
            root.GetProperty("status").GetInt32(),
            root.GetProperty("criticality").GetInt32(),
            root.GetProperty("latitude").GetDouble(),
            root.GetProperty("longitude").GetDouble(),
            root.GetProperty("hasPhoto").GetBoolean());

        return view;
    }

    private async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        JsonDocument document = JsonDocument.Parse(body);

        return document;
    }

    private async Task<string[]> ReadErrorCodesAsync(TestUser user, HttpResponseMessage response)
    {
        string[] codes = await new AccountApiClient(user.Client).ReadErrorCodesAsync(response, cancellationToken);

        return codes;
    }

    private static string ThumbnailNameOf(string blobName)
    {
        return $"{blobName[..^4]}-thumb.jpg";
    }

    private async Task AssertItemsHaveNullEditPermissionsAsync(HttpResponseMessage response, Guid sightingId)
    {
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        using JsonDocument document = JsonDocument.Parse(body);
        List<JsonElement> items = document.RootElement.GetProperty("items").EnumerateArray().ToList();

        Assert.Contains(items, item => item.GetProperty("id").GetGuid() == sightingId && item.GetProperty("isMine").GetBoolean());
        Assert.All(items, item => Assert.Equal(JsonValueKind.Null, item.GetProperty("editPermissions").ValueKind));
    }

    private async Task<JsonElement?> GetEditPermissionsAsync(TestUser user, Guid sightingId)
    {
        using HttpResponseMessage response = await user.Client.GetAsync($"/sightings/{sightingId}", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await ReadEditPermissionsAsync(response);
    }

    private async Task<JsonElement?> ReadEditPermissionsAsync(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement permissions = document.RootElement.GetProperty("editPermissions");

        if (permissions.ValueKind == JsonValueKind.Null)
            return null;

        return permissions.Clone();
    }

    private static int[] AllowedNextStatuses(JsonElement permissions)
    {
        return permissions.GetProperty("allowedNextStatuses").EnumerateArray().Select(status => status.GetInt32()).ToArray();
    }

    private async Task<World> SeedAsync(byte[]? photo = null)
    {
        TestUser alice = await SeedUserAsync(aliceEmail, aliceDisplayName);
        TestUser bob = await SeedUserAsync(bobEmail, bobDisplayName);
        TestUser carol = await SeedUserAsync(carolEmail, carolDisplayName);
        Guid sightingId = await alice.CreateSightingAsync(latitude: latitude, longitude: longitude, photo: photo, cancellationToken: cancellationToken);

        return new World(alice, bob, carol, sightingId);
    }

    private async Task<TestUser> SeedUserAsync(string email, string displayName)
    {
        TestUser user = await factory.CreateUserAsync(email, password, displayName, cancellationToken);
        users.Add(user);

        return user;
    }

    private sealed record World(TestUser Alice, TestUser Bob, TestUser Carol, Guid SightingId);

    private sealed record SightingView(
        string? Name,
        string Description,
        string Behaviour,
        int Status,
        int Criticality,
        double Latitude,
        double Longitude,
        bool HasPhoto);
}

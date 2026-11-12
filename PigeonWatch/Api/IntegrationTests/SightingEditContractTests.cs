using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

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

    public void Dispose()
    {
        foreach (TestUser user in users)
            user.Dispose();

        factory.Dispose();
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

    private async Task<World> SeedAsync()
    {
        TestUser alice = await SeedUserAsync(aliceEmail, aliceDisplayName);
        TestUser bob = await SeedUserAsync(bobEmail, bobDisplayName);
        TestUser carol = await SeedUserAsync(carolEmail, carolDisplayName);
        Guid sightingId = await alice.CreateSightingAsync(latitude: latitude, longitude: longitude, cancellationToken: cancellationToken);

        return new World(alice, bob, carol, sightingId);
    }

    private async Task<TestUser> SeedUserAsync(string email, string displayName)
    {
        TestUser user = await factory.CreateUserAsync(email, password, displayName, cancellationToken);
        users.Add(user);

        return user;
    }

    private sealed record World(TestUser Alice, TestUser Bob, TestUser Carol, Guid SightingId);
}

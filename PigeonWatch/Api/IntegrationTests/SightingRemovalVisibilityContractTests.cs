using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace PigeonWatch.IntegrationTests;

public sealed class SightingRemovalVisibilityContractTests : IDisposable
{
    private const string aliceEmail = "alice.visibility@example.org";
    private const string aliceDisplayName = "AliceVisibility";
    private const string bobEmail = "bob.visibility@example.net";
    private const string bobDisplayName = "BobVisibility";
    private const string carolEmail = "carol.visibility@example.com";
    private const string carolDisplayName = "CarolVisibility";
    private const string adminEmail = "admin.visibility@example.com";
    private const string adminDisplayName = "AdminVisibility";
    private const string password = "Passw0rd!visible1";
    private const string sightingRemoved = "SightingRemoved";
    private const string nearbyUri = "/sightings/nearby?latitude=52.2297&longitude=21.0122";
    private const string listUri = "/sightings/list";
    private const double latitude = 52.2297;
    private const double longitude = 21.0122;

    private readonly ManualTimeProvider clock = new(DateTimeOffset.UtcNow);
    private readonly PigeonWatchApiFactory factory;
    private readonly List<TestUser> users = [];
    private readonly CancellationToken cancellationToken = TestContext.Current.CancellationToken;

    public SightingRemovalVisibilityContractTests()
    {
        factory = new PigeonWatchApiFactory(clock, adminEmails: [adminEmail]);
    }

    [Fact]
    public async Task Nearby_and_list_show_a_removed_sighting_only_to_the_admin_with_its_removal_time()
    {
        World world = await SeedRemovedAsync();

        foreach (TestUser member in new[] { world.Alice, world.Bob, world.Carol })
        {
            IReadOnlyList<Guid> nearby = (await GetNearbyAsync(member)).Select(item => item.GetProperty("id").GetGuid()).ToList();
            ListPage list = await GetListAsync(member);

            Assert.Equal([world.OtherSightingId], nearby);
            Assert.Equal([world.OtherSightingId], list.Ids);
            Assert.Equal(1, list.TotalCount);
        }

        List<JsonElement> adminNearby = await GetNearbyAsync(world.Admin);
        ListPage adminList = await GetListAsync(world.Admin);
        JsonElement removedItem = adminNearby.Single(item => item.GetProperty("id").GetGuid() == world.SightingId);

        Assert.Equal(new[] { world.SightingId, world.OtherSightingId }.Order(), adminNearby.Select(item => item.GetProperty("id").GetGuid()).Order());
        Assert.Equal(world.RemovedAt, removedItem.GetProperty("removedAt").GetDateTimeOffset().UtcDateTime);
        Assert.Equal(new[] { world.SightingId, world.OtherSightingId }.Order(), adminList.Ids.Order());
        Assert.Equal(2, adminList.TotalCount);
        Assert.Equal(world.RemovedAt, adminList.RemovedAt[world.SightingId]);
    }

    [Fact]
    public async Task Mine_still_lists_the_removed_sighting_for_its_reporter_with_its_removal_time()
    {
        World world = await SeedRemovedAsync();

        using HttpResponseMessage response = await world.Alice.Client.GetAsync("/sightings/mine", cancellationToken);
        using JsonDocument mine = await ReadJsonAsync(response);
        JsonElement item = Assert.Single(mine.RootElement.GetProperty("items").EnumerateArray());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(world.SightingId, item.GetProperty("id").GetGuid());
        Assert.Equal(world.RemovedAt, item.GetProperty("removedAt").GetDateTimeOffset().UtcDateTime);
    }

    [Fact]
    public async Task Detail_photo_and_thumbnail_of_a_removed_sighting_answer_only_the_reporter_and_the_admin()
    {
        World world = await SeedRemovedAsync();

        using HttpResponseMessage aliceDetail = await world.Alice.Client.GetAsync(SightingUri(world.SightingId), cancellationToken);
        using JsonDocument aliceSighting = await ReadJsonAsync(aliceDetail);
        using HttpResponseMessage adminDetail = await world.Admin.Client.GetAsync(SightingUri(world.SightingId), cancellationToken);
        using JsonDocument adminSighting = await ReadJsonAsync(adminDetail);

        Assert.Equal(HttpStatusCode.OK, aliceDetail.StatusCode);
        Assert.True(aliceSighting.RootElement.GetProperty("isMine").GetBoolean());
        Assert.Equal(JsonValueKind.Null, aliceSighting.RootElement.GetProperty("editPermissions").ValueKind);
        Assert.Equal(world.RemovedAt, aliceSighting.RootElement.GetProperty("removedAt").GetDateTimeOffset().UtcDateTime);
        Assert.Equal(HttpStatusCode.OK, adminDetail.StatusCode);
        Assert.Equal(JsonValueKind.Null, adminSighting.RootElement.GetProperty("editPermissions").ValueKind);
        Assert.Equal(world.RemovedAt, adminSighting.RootElement.GetProperty("removedAt").GetDateTimeOffset().UtcDateTime);

        foreach (TestUser viewer in new[] { world.Alice, world.Admin })
        {
            using HttpResponseMessage photo = await viewer.Client.GetAsync($"{SightingUri(world.SightingId)}/photo", cancellationToken);
            using HttpResponseMessage thumbnail = await viewer.Client.GetAsync($"{SightingUri(world.SightingId)}/photo/thumbnail", cancellationToken);

            Assert.Equal(HttpStatusCode.OK, photo.StatusCode);
            Assert.Equal(HttpStatusCode.OK, thumbnail.StatusCode);
            Assert.Equal("image/jpeg", photo.Content.Headers.ContentType?.MediaType);
            Assert.Equal("image/jpeg", thumbnail.Content.Headers.ContentType?.MediaType);
        }

        foreach (TestUser member in new[] { world.Bob, world.Carol })
        {
            using HttpResponseMessage detail = await member.Client.GetAsync(SightingUri(world.SightingId), cancellationToken);
            using HttpResponseMessage photo = await member.Client.GetAsync($"{SightingUri(world.SightingId)}/photo", cancellationToken);
            using HttpResponseMessage thumbnail = await member.Client.GetAsync($"{SightingUri(world.SightingId)}/photo/thumbnail", cancellationToken);

            Assert.Equal(HttpStatusCode.NotFound, detail.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, photo.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, thumbnail.StatusCode);
        }
    }

    [Fact]
    public async Task Comments_of_a_removed_sighting_are_readable_by_the_reporter_and_the_admin_and_writable_by_nobody()
    {
        World world = await SeedRemovedAsync();

        using HttpResponseMessage aliceList = await world.Alice.Client.GetAsync(CommentsUri(world.SightingId), cancellationToken);
        using JsonDocument alicePage = await ReadJsonAsync(aliceList);
        using HttpResponseMessage adminList = await world.Admin.Client.GetAsync(CommentsUri(world.SightingId), cancellationToken);
        using HttpResponseMessage alicePost = await PostCommentAsync(world.Alice, world.SightingId, "Still here");
        using HttpResponseMessage adminPost = await PostCommentAsync(world.Admin, world.SightingId, "Admin note");
        using HttpResponseMessage aliceEdit = await world.Alice.Client.PutAsJsonAsync($"{CommentsUri(world.SightingId)}/{world.CommentId}", new { text = "Edited" }, cancellationToken);
        using HttpResponseMessage aliceDelete = await world.Alice.Client.DeleteAsync($"{CommentsUri(world.SightingId)}/{world.CommentId}", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, aliceList.StatusCode);
        Assert.Equal(1, alicePage.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal(HttpStatusCode.OK, adminList.StatusCode);
        await AssertSightingRemovedAsync(world.Alice, alicePost);
        await AssertSightingRemovedAsync(world.Admin, adminPost);
        await AssertSightingRemovedAsync(world.Alice, aliceEdit);
        await AssertSightingRemovedAsync(world.Alice, aliceDelete);

        foreach (TestUser member in new[] { world.Bob, world.Carol })
        {
            using HttpResponseMessage list = await member.Client.GetAsync(CommentsUri(world.SightingId), cancellationToken);
            using HttpResponseMessage post = await PostCommentAsync(member, world.SightingId, "Hello");

            Assert.Equal(HttpStatusCode.NotFound, list.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, post.StatusCode);
        }

        using HttpResponseMessage readBack = await world.Admin.Client.GetAsync(CommentsUri(world.SightingId), cancellationToken);
        using JsonDocument readBackPage = await ReadJsonAsync(readBack);
        JsonElement comment = Assert.Single(readBackPage.RootElement.GetProperty("items").EnumerateArray());

        Assert.Equal(1, readBackPage.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal(world.CommentId, comment.GetProperty("id").GetGuid());
        Assert.Equal("Seen near the fountain", comment.GetProperty("text").GetString());
        Assert.False(comment.GetProperty("isDeleted").GetBoolean());
    }

    [Fact]
    public async Task Editing_a_removed_sighting_is_sighting_removed_for_the_reporter_404_for_members_403_for_the_admin_and_writes_nothing()
    {
        World world = await SeedRemovedAsync();
        int savedBlobs = factory.PhotoStore.SavedBlobNames.Count;
        Dictionary<string, string> fields = SightingForm.UpdateFields(status: 2, description: "Changed after removal");

        using HttpResponseMessage alice = await world.Alice.UpdateSightingAsync(world.SightingId, fields, TestPhotos.JpegWithExif(), cancellationToken);
        using HttpResponseMessage bob = await world.Bob.UpdateSightingAsync(world.SightingId, fields, cancellationToken: cancellationToken);
        using HttpResponseMessage carol = await world.Carol.UpdateSightingAsync(world.SightingId, fields, cancellationToken: cancellationToken);
        using HttpResponseMessage admin = await world.Admin.UpdateSightingAsync(world.SightingId, fields, cancellationToken: cancellationToken);
        using HttpResponseMessage readBack = await world.Alice.Client.GetAsync(SightingUri(world.SightingId), cancellationToken);
        using JsonDocument stored = await ReadJsonAsync(readBack);

        await AssertSightingRemovedAsync(world.Alice, alice);
        Assert.Equal(HttpStatusCode.NotFound, bob.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, carol.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, admin.StatusCode);
        Assert.Equal("A pigeon with a bent wing near the fountain.", stored.RootElement.GetProperty("description").GetString());
        Assert.Equal(1, stored.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(savedBlobs, factory.PhotoStore.SavedBlobNames.Count);
    }

    [Fact]
    public async Task Threads_of_a_removed_sighting_stay_readable_by_their_participants_and_take_no_new_messages()
    {
        World world = await SeedRemovedAsync();
        int storedMessages = factory.ReadSightingMessageAuditUsers().Count;

        foreach (TestUser participant in new[] { world.Alice, world.Bob })
        {
            using HttpResponseMessage inbox = await participant.Client.GetAsync("/threads", cancellationToken);
            using JsonDocument inboxPage = await ReadJsonAsync(inbox);
            JsonElement thread = Assert.Single(inboxPage.RootElement.GetProperty("items").EnumerateArray());
            using HttpResponseMessage messages = await participant.Client.GetAsync(MessagesUri(world.SightingId, world.ThreadId), cancellationToken);
            using JsonDocument messagePage = await ReadJsonAsync(messages);
            using HttpResponseMessage markRead = await participant.Client.PutAsJsonAsync(
                $"{ThreadsUri(world.SightingId)}/{world.ThreadId}/read",
                new { seenAt = clock.GetUtcNow().ToString("O") },
                cancellationToken);
            using HttpResponseMessage reply = await participant.Client.PostAsJsonAsync(MessagesUri(world.SightingId, world.ThreadId), new { text = "Anyone there?" }, cancellationToken);

            Assert.Equal(HttpStatusCode.OK, inbox.StatusCode);
            Assert.Equal(world.ThreadId, thread.GetProperty("id").GetGuid());
            Assert.True(thread.GetProperty("isSightingRemoved").GetBoolean());
            Assert.Equal(HttpStatusCode.OK, messages.StatusCode);
            Assert.Equal(2, messagePage.RootElement.GetProperty("totalCount").GetInt32());
            Assert.Equal(HttpStatusCode.NoContent, markRead.StatusCode);
            await AssertSightingRemovedAsync(participant, reply);
        }

        using HttpResponseMessage bobThreadList = await world.Bob.Client.GetAsync(ThreadsUri(world.SightingId), cancellationToken);
        using JsonDocument bobThreadPage = await ReadJsonAsync(bobThreadList);
        using HttpResponseMessage carolThreadList = await world.Carol.Client.GetAsync(ThreadsUri(world.SightingId), cancellationToken);
        using HttpResponseMessage carolMessages = await world.Carol.Client.GetAsync(MessagesUri(world.SightingId, world.ThreadId), cancellationToken);
        using HttpResponseMessage carolReply = await world.Carol.Client.PostAsJsonAsync(MessagesUri(world.SightingId, world.ThreadId), new { text = "Let me in" }, cancellationToken);
        using HttpResponseMessage carolStart = await world.Carol.Client.PostAsJsonAsync(ThreadsUri(world.SightingId), new { text = "Hello" }, cancellationToken);
        using HttpResponseMessage bobStart = await world.Bob.Client.PostAsJsonAsync(ThreadsUri(world.SightingId), new { text = "Hello again" }, cancellationToken);
        using HttpResponseMessage adminStart = await world.Admin.Client.PostAsJsonAsync(ThreadsUri(world.SightingId), new { text = "Admin here" }, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, bobThreadList.StatusCode);
        Assert.Equal(world.ThreadId, Assert.Single(bobThreadPage.RootElement.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.NotFound, carolThreadList.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, carolMessages.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, carolReply.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, carolStart.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, bobStart.StatusCode);
        await AssertSightingRemovedAsync(world.Admin, adminStart);
        Assert.Equal(storedMessages, factory.ReadSightingMessageAuditUsers().Count);
        Assert.Single(factory.ReadSightingThreadAuditUsers());
    }

    [Fact]
    public async Task Restore_returns_every_path_to_the_behaviour_of_a_visible_sighting()
    {
        World world = await SeedRemovedAsync();
        using HttpResponseMessage restore = await world.Admin.Client.PostAsync($"{SightingUri(world.SightingId)}/restore", null, cancellationToken);
        clock.Advance(TimeSpan.FromMinutes(2));

        List<JsonElement> bobNearby = await GetNearbyAsync(world.Bob);
        ListPage carolList = await GetListAsync(world.Carol);
        using HttpResponseMessage bobDetail = await world.Bob.Client.GetAsync(SightingUri(world.SightingId), cancellationToken);
        using JsonDocument bobSighting = await ReadJsonAsync(bobDetail);
        using HttpResponseMessage bobPhoto = await world.Bob.Client.GetAsync($"{SightingUri(world.SightingId)}/photo", cancellationToken);
        using HttpResponseMessage carolThumbnail = await world.Carol.Client.GetAsync($"{SightingUri(world.SightingId)}/photo/thumbnail", cancellationToken);
        using HttpResponseMessage carolComments = await world.Carol.Client.GetAsync(CommentsUri(world.SightingId), cancellationToken);
        using HttpResponseMessage bobComment = await PostCommentAsync(world.Bob, world.SightingId, "Glad it is back");
        using HttpResponseMessage aliceEdit = await world.Alice.UpdateSightingAsync(world.SightingId, SightingForm.UpdateFields(status: 2), cancellationToken: cancellationToken);
        using JsonDocument aliceEdited = await ReadJsonAsync(aliceEdit);
        using HttpResponseMessage adminEdit = await world.Admin.UpdateSightingAsync(world.SightingId, SightingForm.UpdateFields(status: 2), cancellationToken: cancellationToken);
        using HttpResponseMessage bobInbox = await world.Bob.Client.GetAsync("/threads", cancellationToken);
        using JsonDocument bobInboxPage = await ReadJsonAsync(bobInbox);
        using HttpResponseMessage bobReply = await world.Bob.Client.PostAsJsonAsync(MessagesUri(world.SightingId, world.ThreadId), new { text = "Back again" }, cancellationToken);
        using HttpResponseMessage carolStart = await world.Carol.Client.PostAsJsonAsync(ThreadsUri(world.SightingId), new { text = "Can I help?" }, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, restore.StatusCode);
        Assert.Contains(world.SightingId, bobNearby.Select(item => item.GetProperty("id").GetGuid()));
        Assert.Contains(world.SightingId, carolList.Ids);
        Assert.Equal(2, carolList.TotalCount);
        Assert.Equal(HttpStatusCode.OK, bobDetail.StatusCode);
        Assert.Equal(JsonValueKind.Null, bobSighting.RootElement.GetProperty("removedAt").ValueKind);
        Assert.Equal(HttpStatusCode.OK, bobPhoto.StatusCode);
        Assert.Equal(HttpStatusCode.OK, carolThumbnail.StatusCode);
        Assert.Equal(HttpStatusCode.OK, carolComments.StatusCode);
        Assert.Equal(HttpStatusCode.Created, bobComment.StatusCode);
        Assert.Equal(HttpStatusCode.OK, aliceEdit.StatusCode);
        Assert.Equal(2, aliceEdited.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(JsonValueKind.Object, aliceEdited.RootElement.GetProperty("editPermissions").ValueKind);
        Assert.Equal(HttpStatusCode.Forbidden, adminEdit.StatusCode);
        Assert.False(Assert.Single(bobInboxPage.RootElement.GetProperty("items").EnumerateArray()).GetProperty("isSightingRemoved").GetBoolean());
        Assert.Equal(HttpStatusCode.Created, bobReply.StatusCode);
        Assert.Equal(HttpStatusCode.Created, carolStart.StatusCode);
    }

    public void Dispose()
    {
        foreach (TestUser user in users)
            user.Dispose();

        factory.Dispose();
    }

    private static string SightingUri(Guid sightingId)
    {
        return $"/sightings/{sightingId}";
    }

    private static string CommentsUri(Guid sightingId)
    {
        return $"/sightings/{sightingId}/comments";
    }

    private static string ThreadsUri(Guid sightingId)
    {
        return $"/sightings/{sightingId}/threads";
    }

    private static string MessagesUri(Guid sightingId, Guid threadId)
    {
        return $"/sightings/{sightingId}/threads/{threadId}/messages";
    }

    private async Task AssertSightingRemovedAsync(TestUser user, HttpResponseMessage response)
    {
        string[] codes = await new AccountApiClient(user.Client).ReadErrorCodesAsync(response, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal([sightingRemoved], codes);
    }

    private async Task<HttpResponseMessage> PostCommentAsync(TestUser user, Guid sightingId, string text)
    {
        HttpResponseMessage response = await user.Client.PostAsJsonAsync(CommentsUri(sightingId), new { text }, cancellationToken);

        return response;
    }

    private async Task<List<JsonElement>> GetNearbyAsync(TestUser user)
    {
        using HttpResponseMessage response = await user.Client.GetAsync(nearbyUri, cancellationToken);
        using JsonDocument document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return document.RootElement.GetProperty("items").EnumerateArray().Select(item => item.Clone()).ToList();
    }

    private async Task<ListPage> GetListAsync(TestUser user)
    {
        using HttpResponseMessage response = await user.Client.PostAsJsonAsync(listUri, new { latitude, longitude }, cancellationToken);
        using JsonDocument document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        List<JsonElement> items = document.RootElement.GetProperty("items").EnumerateArray().ToList();
        List<Guid> ids = items.Select(item => item.GetProperty("id").GetGuid()).ToList();
        Dictionary<Guid, DateTime?> removedAt = items.ToDictionary(
            item => item.GetProperty("id").GetGuid(),
            item => item.GetProperty("removedAt").ValueKind == JsonValueKind.Null ? (DateTime?)null : item.GetProperty("removedAt").GetDateTimeOffset().UtcDateTime);

        return new ListPage(ids, document.RootElement.GetProperty("totalCount").GetInt32(), removedAt);
    }

    private async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        JsonDocument document = JsonDocument.Parse(body);

        return document;
    }

    private async Task<World> SeedRemovedAsync()
    {
        TestUser alice = Track(await factory.CreateUserAsync(aliceEmail, password, aliceDisplayName, cancellationToken));
        TestUser bob = Track(await factory.CreateUserAsync(bobEmail, password, bobDisplayName, cancellationToken));
        TestUser carol = Track(await factory.CreateUserAsync(carolEmail, password, carolDisplayName, cancellationToken));
        TestUser admin = Track(await factory.CreateAdminAsync(adminEmail, password, adminDisplayName, cancellationToken));
        Guid sightingId = await alice.CreateSightingAsync(photo: TestPhotos.JpegWithExif(), cancellationToken: cancellationToken);
        Guid otherSightingId = await bob.CreateSightingAsync(cancellationToken: cancellationToken);
        Guid commentId = await alice.CreateCommentAsync(sightingId, "Seen near the fountain", cancellationToken);
        Guid threadId = await bob.StartThreadAsync(sightingId, "I can help catch it", cancellationToken);
        clock.Advance(TimeSpan.FromSeconds(1));
        await alice.ReplyAsync(sightingId, threadId, "Thank you", cancellationToken);
        clock.Advance(TimeSpan.FromSeconds(1));
        DateTime removedAt = clock.GetUtcNow().UtcDateTime;
        using HttpResponseMessage remove = await admin.Client.DeleteAsync(SightingUri(sightingId), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, remove.StatusCode);

        return new World(alice, bob, carol, admin, sightingId, otherSightingId, commentId, threadId, removedAt);
    }

    private TestUser Track(TestUser user)
    {
        users.Add(user);

        return user;
    }

    private sealed record World(
        TestUser Alice,
        TestUser Bob,
        TestUser Carol,
        TestUser Admin,
        Guid SightingId,
        Guid OtherSightingId,
        Guid CommentId,
        Guid ThreadId,
        DateTime RemovedAt);

    private sealed record ListPage(IReadOnlyList<Guid> Ids, int TotalCount, IReadOnlyDictionary<Guid, DateTime?> RemovedAt);
}

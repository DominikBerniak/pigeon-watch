using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace PigeonWatch.IntegrationTests;

public sealed class SightingCommentContractTests : IDisposable
{
    private const string aliceEmail = "alice.commenter@example.org";
    private const string aliceDisplayName = "AliceCommenter";
    private const string bobEmail = "bob.commenter@example.net";
    private const string bobDisplayName = "BobCommenter";
    private const string password = "Passw0rd!comment1";

    private readonly ManualTimeProvider clock = new(DateTimeOffset.UtcNow);
    private readonly PigeonWatchApiFactory factory;
    private readonly List<TestUser> users = [];
    private readonly CancellationToken cancellationToken = TestContext.Current.CancellationToken;

    public SightingCommentContractTests()
    {
        factory = new PigeonWatchApiFactory(clock);
    }

    [Fact]
    public async Task Bob_can_comment_on_alices_sighting_and_alice_can_comment_on_her_own()
    {
        (TestUser alice, TestUser bob, Guid sightingId) = await SeedAsync();

        using HttpResponseMessage bobPost = await PostAsync(bob, sightingId, "  Seen near the fountain  ");
        CommentView bobComment = await ReadCommentAsync(bobPost);
        using HttpResponseMessage alicePost = await PostAsync(alice, sightingId, "Thanks, on my way");
        CommentView aliceComment = await ReadCommentAsync(alicePost);

        Assert.Equal(HttpStatusCode.Created, bobPost.StatusCode);
        Assert.Equal($"/sightings/{sightingId}/comments", bobPost.Headers.Location!.AbsolutePath, ignoreCase: true);
        Assert.Equal("Seen near the fountain", bobComment.Text);
        Assert.Equal(bobDisplayName, bobComment.AuthorDisplayName);
        Assert.True(bobComment.IsMine);
        Assert.False(bobComment.IsDeleted);
        Assert.Null(bobComment.EditedAt);
        Assert.Equal(HttpStatusCode.Created, alicePost.StatusCode);
        Assert.Equal(aliceDisplayName, aliceComment.AuthorDisplayName);
        Assert.True(aliceComment.IsMine);
    }

    [Fact]
    public async Task List_is_newest_first_in_pages_of_20_with_the_total_count_and_rejects_page_0()
    {
        (TestUser alice, TestUser bob, Guid sightingId) = await SeedAsync();
        List<Guid> created = [];

        for (int index = 0; index < 21; index++)
        {
            using HttpResponseMessage post = await PostAsync(bob, sightingId, $"Comment {index}");
            created.Add((await ReadCommentAsync(post)).Id);
            clock.Advance(TimeSpan.FromSeconds(60));
        }

        created.Reverse();

        using HttpResponseMessage first = await alice.Client.GetAsync($"/sightings/{sightingId}/comments", cancellationToken);
        PageView firstPage = await ReadPageAsync(first);
        using HttpResponseMessage second = await alice.Client.GetAsync($"/sightings/{sightingId}/comments?page=2", cancellationToken);
        PageView secondPage = await ReadPageAsync(second);
        using HttpResponseMessage zero = await alice.Client.GetAsync($"/sightings/{sightingId}/comments?page=0", cancellationToken);
        string[] zeroCodes = await new AccountApiClient(alice.Client).ReadErrorCodesAsync(zero, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("no-store, private", NormalizedCacheControl(first));
        Assert.Equal(created.Take(20), firstPage.Items.Select(item => item.Id));
        Assert.Equal(21, firstPage.TotalCount);
        Assert.All(firstPage.Items, item => Assert.False(item.IsMine));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(created.Skip(20), secondPage.Items.Select(item => item.Id));
        Assert.Equal(21, secondPage.TotalCount);
        Assert.Equal(HttpStatusCode.BadRequest, zero.StatusCode);
        Assert.Equal(["InvalidPage"], zeroCodes);
    }

    [Fact]
    public async Task Unknown_sighting_or_comment_is_a_404()
    {
        (TestUser _, TestUser bob, Guid sightingId) = await SeedAsync();
        Guid unknownSighting = Guid.NewGuid();
        Guid commentId = await bob.CreateCommentAsync(sightingId, "Seen near the fountain", cancellationToken);
        clock.Advance(TimeSpan.FromSeconds(60));

        using HttpResponseMessage list = await bob.Client.GetAsync($"/sightings/{unknownSighting}/comments", cancellationToken);
        using HttpResponseMessage post = await PostAsync(bob, unknownSighting, "Hello");
        using HttpResponseMessage edit = await PutAsync(bob, sightingId, Guid.NewGuid(), "Hello");
        using HttpResponseMessage delete = await bob.Client.DeleteAsync($"/sightings/{sightingId}/comments/{Guid.NewGuid()}", cancellationToken);
        using HttpResponseMessage editUnderOtherSighting = await PutAsync(bob, unknownSighting, commentId, "Hello");

        Assert.Equal(HttpStatusCode.NotFound, list.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, post.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, edit.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, editUnderOtherSighting.StatusCode);
    }

    [Fact]
    public async Task Bob_cannot_edit_or_delete_alices_comment_and_it_stays_unchanged()
    {
        (TestUser alice, TestUser bob, Guid sightingId) = await SeedAsync();
        Guid commentId = await alice.CreateCommentAsync(sightingId, "Original text", cancellationToken);
        clock.Advance(TimeSpan.FromSeconds(120));

        using HttpResponseMessage edit = await PutAsync(bob, sightingId, commentId, "Hijacked");
        using HttpResponseMessage delete = await bob.Client.DeleteAsync($"/sightings/{sightingId}/comments/{commentId}", cancellationToken);
        using HttpResponseMessage readBack = await alice.Client.GetAsync($"/sightings/{sightingId}/comments", cancellationToken);
        PageView page = await ReadPageAsync(readBack);

        Assert.Equal(HttpStatusCode.Forbidden, edit.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
        CommentView comment = Assert.Single(page.Items);
        Assert.Equal(commentId, comment.Id);
        Assert.Equal("Original text", comment.Text);
        Assert.Null(comment.EditedAt);
        Assert.False(comment.IsDeleted);
        Assert.True(comment.IsMine);
    }

    [Fact]
    public async Task Deleted_comment_is_a_placeholder_that_stays_in_the_count_and_cannot_be_edited_or_deleted_again()
    {
        (TestUser alice, TestUser bob, Guid sightingId) = await SeedAsync();
        Guid commentId = await bob.CreateCommentAsync(sightingId, "Seen near the fountain", cancellationToken);
        await alice.CreateCommentAsync(sightingId, "Thanks", cancellationToken);
        clock.Advance(TimeSpan.FromSeconds(120));

        using HttpResponseMessage delete = await bob.Client.DeleteAsync($"/sightings/{sightingId}/comments/{commentId}", cancellationToken);
        CommentView placeholder = await ReadCommentAsync(delete);
        using HttpResponseMessage deleteAgain = await bob.Client.DeleteAsync($"/sightings/{sightingId}/comments/{commentId}", cancellationToken);
        using HttpResponseMessage editDeleted = await PutAsync(bob, sightingId, commentId, "Back again");
        using HttpResponseMessage list = await alice.Client.GetAsync($"/sightings/{sightingId}/comments", cancellationToken);
        PageView page = await ReadPageAsync(list);

        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
        Assert.Equal(commentId, placeholder.Id);
        Assert.True(placeholder.IsDeleted);
        Assert.Null(placeholder.Text);
        Assert.Null(placeholder.AuthorDisplayName);
        Assert.Null(placeholder.EditedAt);
        Assert.False(placeholder.IsMine);
        Assert.Equal(HttpStatusCode.NotFound, deleteAgain.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, editDeleted.StatusCode);
        Assert.Equal(2, page.TotalCount);
        CommentView listed = Assert.Single(page.Items, item => item.Id == commentId);
        Assert.True(listed.IsDeleted);
        Assert.Null(listed.Text);
        Assert.Null(listed.AuthorDisplayName);
    }

    [Fact]
    public async Task Edit_sets_edited_at_and_the_new_text()
    {
        (TestUser _, TestUser bob, Guid sightingId) = await SeedAsync();
        Guid commentId = await bob.CreateCommentAsync(sightingId, "Seen near the fountain", cancellationToken);
        clock.Advance(TimeSpan.FromSeconds(60));

        using HttpResponseMessage edit = await PutAsync(bob, sightingId, commentId, "  Seen near the old fountain ");
        CommentView edited = await ReadCommentAsync(edit);

        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        Assert.Equal("Seen near the old fountain", edited.Text);
        Assert.NotNull(edited.EditedAt);
        Assert.True(edited.IsMine);
        Assert.False(edited.IsDeleted);
    }

    [Fact]
    public async Task Delete_does_not_count_as_a_submission_for_the_cooldown()
    {
        (TestUser _, TestUser bob, Guid sightingId) = await SeedAsync();
        Guid commentId = await bob.CreateCommentAsync(sightingId, "Seen near the fountain", cancellationToken);
        clock.Advance(TimeSpan.FromSeconds(60));

        using HttpResponseMessage delete = await bob.Client.DeleteAsync($"/sightings/{sightingId}/comments/{commentId}", cancellationToken);
        clock.Advance(TimeSpan.FromSeconds(1));
        using HttpResponseMessage post = await PostAsync(bob, sightingId, "Posted again");

        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
        Assert.Equal(HttpStatusCode.Created, post.StatusCode);
    }

    [Fact]
    public async Task Post_30_seconds_after_a_post_is_blocked_with_retry_after_30()
    {
        (TestUser _, TestUser bob, Guid sightingId) = await SeedAsync();
        await bob.CreateCommentAsync(sightingId, "First", cancellationToken);
        clock.Advance(TimeSpan.FromSeconds(30));

        using HttpResponseMessage blocked = await PostAsync(bob, sightingId, "Second");
        string body = await blocked.Content.ReadAsStringAsync(cancellationToken);
        using JsonDocument problem = JsonDocument.Parse(body);
        string[] codes = problem.RootElement.GetProperty("errors").EnumerateObject().Select(property => property.Name).ToArray();
        clock.Advance(TimeSpan.FromSeconds(30));
        using HttpResponseMessage allowed = await PostAsync(bob, sightingId, "Second");

        Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
        Assert.Equal(["CooldownActive"], codes);
        Assert.Equal(30, problem.RootElement.GetProperty("retryAfterSeconds").GetInt32());
        Assert.Equal(TimeSpan.FromSeconds(30), blocked.Headers.RetryAfter?.Delta);
        Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);
    }

    [Fact]
    public async Task Edit_30_seconds_after_a_post_is_blocked()
    {
        (TestUser _, TestUser bob, Guid sightingId) = await SeedAsync();
        Guid commentId = await bob.CreateCommentAsync(sightingId, "First", cancellationToken);
        clock.Advance(TimeSpan.FromSeconds(30));

        using HttpResponseMessage blocked = await PutAsync(bob, sightingId, commentId, "Edited");
        string[] codes = await new AccountApiClient(bob.Client).ReadErrorCodesAsync(blocked, cancellationToken);
        clock.Advance(TimeSpan.FromSeconds(30));
        using HttpResponseMessage allowed = await PutAsync(bob, sightingId, commentId, "Edited");

        Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
        Assert.Equal(["CooldownActive"], codes);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    [Fact]
    public async Task Post_after_deleting_the_previous_comment_is_still_blocked_until_60_seconds()
    {
        (TestUser _, TestUser bob, Guid sightingId) = await SeedAsync();
        Guid commentId = await bob.CreateCommentAsync(sightingId, "First", cancellationToken);

        using HttpResponseMessage delete = await bob.Client.DeleteAsync($"/sightings/{sightingId}/comments/{commentId}", cancellationToken);
        clock.Advance(TimeSpan.FromSeconds(30));
        using HttpResponseMessage blocked = await PostAsync(bob, sightingId, "Second");
        string[] codes = await new AccountApiClient(bob.Client).ReadErrorCodesAsync(blocked, cancellationToken);
        clock.Advance(TimeSpan.FromSeconds(30));
        using HttpResponseMessage allowed = await PostAsync(bob, sightingId, "Second");

        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
        Assert.Equal(["CooldownActive"], codes);
        Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);
    }

    [Fact]
    public async Task Sighting_and_comment_cooldowns_are_separate_clocks()
    {
        (TestUser alice, TestUser _, Guid sightingId) = await SeedAsync();
        clock.Advance(TimeSpan.FromSeconds(1));

        using HttpResponseMessage commentAfterReport = await PostAsync(alice, sightingId, "Update from the reporter");
        clock.Advance(TimeSpan.FromSeconds(60));
        await alice.CreateCommentAsync(sightingId, "Another update", cancellationToken);
        clock.Advance(TimeSpan.FromSeconds(1));
        Guid reportAfterComment = await alice.CreateSightingAsync(cancellationToken: cancellationToken);

        Assert.Equal(HttpStatusCode.Created, commentAfterReport.StatusCode);
        Assert.NotEqual(Guid.Empty, reportAfterComment);
    }

    [Fact]
    public async Task Whitespace_only_text_is_comment_text_required_and_501_characters_is_a_400()
    {
        (TestUser _, TestUser bob, Guid sightingId) = await SeedAsync();

        using HttpResponseMessage whitespace = await PostAsync(bob, sightingId, "   \n\t ");
        string[] codes = await new AccountApiClient(bob.Client).ReadErrorCodesAsync(whitespace, cancellationToken);
        using HttpResponseMessage tooLong = await PostAsync(bob, sightingId, new string('x', 501));
        using HttpResponseMessage atLimit = await PostAsync(bob, sightingId, new string('x', 500));

        Assert.Equal(HttpStatusCode.BadRequest, whitespace.StatusCode);
        Assert.Equal(["CommentTextRequired"], codes);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal(HttpStatusCode.Created, atLimit.StatusCode);
    }

    [Fact]
    public async Task Comment_responses_carry_no_author_id_audit_user_or_user_id()
    {
        (TestUser alice, TestUser bob, Guid sightingId) = await SeedAsync();
        using HttpResponseMessage post = await PostAsync(bob, sightingId, "Seen near the fountain");
        Guid commentId = (await ReadCommentAsync(post)).Id;
        clock.Advance(TimeSpan.FromSeconds(60));
        using HttpResponseMessage edit = await PutAsync(bob, sightingId, commentId, "Seen near the old fountain");
        using HttpResponseMessage listAsAlice = await alice.Client.GetAsync($"/sightings/{sightingId}/comments", cancellationToken);
        using HttpResponseMessage listAsBob = await bob.Client.GetAsync($"/sightings/{sightingId}/comments", cancellationToken);
        using HttpResponseMessage delete = await bob.Client.DeleteAsync($"/sightings/{sightingId}/comments/{commentId}", cancellationToken);
        List<string> bodies =
        [
            await post.Content.ReadAsStringAsync(cancellationToken),
            await edit.Content.ReadAsStringAsync(cancellationToken),
            await listAsAlice.Content.ReadAsStringAsync(cancellationToken),
            await listAsBob.Content.ReadAsStringAsync(cancellationToken),
            await delete.Content.ReadAsStringAsync(cancellationToken)
        ];
        string[] forbidden =
        [
            "authorId",
            "createUser",
            "updateUser",
            "email",
            alice.Id.ToString("D"),
            alice.Id.ToString("N"),
            bob.Id.ToString("D"),
            bob.Id.ToString("N"),
            aliceEmail,
            bobEmail
        ];

        Assert.Equal(HttpStatusCode.Created, post.StatusCode);
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        Assert.Equal(HttpStatusCode.OK, listAsAlice.StatusCode);
        Assert.Equal(HttpStatusCode.OK, listAsBob.StatusCode);
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);

        foreach (string body in bodies)
        {
            foreach (string needle in forbidden)
                Assert.DoesNotContain(needle, body, StringComparison.OrdinalIgnoreCase);
        }
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

    private async Task<(TestUser Alice, TestUser Bob, Guid SightingId)> SeedAsync()
    {
        TestUser alice = await factory.CreateUserAsync(aliceEmail, password, aliceDisplayName, cancellationToken);
        users.Add(alice);
        TestUser bob = await factory.CreateUserAsync(bobEmail, password, bobDisplayName, cancellationToken);
        users.Add(bob);
        Guid sightingId = await alice.CreateSightingAsync(cancellationToken: cancellationToken);

        return (alice, bob, sightingId);
    }

    private async Task<HttpResponseMessage> PostAsync(TestUser user, Guid sightingId, string text)
    {
        HttpResponseMessage response = await user.Client.PostAsJsonAsync($"/sightings/{sightingId}/comments", new { text }, cancellationToken);

        return response;
    }

    private async Task<HttpResponseMessage> PutAsync(TestUser user, Guid sightingId, Guid commentId, string text)
    {
        HttpResponseMessage response = await user.Client.PutAsJsonAsync($"/sightings/{sightingId}/comments/{commentId}", new { text }, cancellationToken);

        return response;
    }

    private async Task<CommentView> ReadCommentAsync(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        using JsonDocument document = JsonDocument.Parse(body);

        return ToComment(document.RootElement);
    }

    private async Task<PageView> ReadPageAsync(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        using JsonDocument document = JsonDocument.Parse(body);
        List<CommentView> items = document.RootElement
            .GetProperty("items")
            .EnumerateArray()
            .Select(ToComment)
            .ToList();
        int totalCount = document.RootElement.GetProperty("totalCount").GetInt32();

        return new PageView(items, totalCount);
    }

    private static CommentView ToComment(JsonElement element)
    {
        JsonElement editedAt = element.GetProperty("editedAt");

        return new CommentView(
            element.GetProperty("id").GetGuid(),
            element.GetProperty("text").GetString(),
            element.GetProperty("authorDisplayName").GetString(),
            editedAt.ValueKind == JsonValueKind.Null ? null : editedAt.GetDateTime(),
            element.GetProperty("isDeleted").GetBoolean(),
            element.GetProperty("isMine").GetBoolean());
    }

    private sealed record CommentView(Guid Id, string? Text, string? AuthorDisplayName, DateTime? EditedAt, bool IsDeleted, bool IsMine);

    private sealed record PageView(IReadOnlyList<CommentView> Items, int TotalCount);
}

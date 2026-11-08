using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace PigeonWatch.IntegrationTests;

public sealed class SightingThreadContractTests : IDisposable
{
    private const string aliceEmail = "alice.threads@example.org";
    private const string aliceDisplayName = "AliceThreads";
    private const string bobEmail = "bob.threads@example.net";
    private const string bobDisplayName = "BobThreads";
    private const string carolEmail = "carol.threads@example.com";
    private const string carolDisplayName = "CarolThreads";
    private const string password = "Passw0rd!thread1";

    private readonly ManualTimeProvider clock = new(DateTimeOffset.UtcNow);
    private readonly PigeonWatchApiFactory factory;
    private readonly List<TestUser> users = [];
    private readonly CancellationToken cancellationToken = TestContext.Current.CancellationToken;

    public SightingThreadContractTests()
    {
        factory = new PigeonWatchApiFactory(clock);
    }

    [Fact]
    public async Task Bob_starting_a_thread_answers_201_with_a_location_and_his_first_message()
    {
        World world = await SeedAsync();

        using HttpResponseMessage start = await StartAsync(world.Bob, world.SightingId, new { text = "  I can help catch it  " });
        StartView started = await ReadStartAsync(start);

        Assert.Equal(HttpStatusCode.Created, start.StatusCode);
        Assert.Equal($"/sightings/{world.SightingId}/threads/{started.Thread.Id}/messages", start.Headers.Location!.AbsolutePath, ignoreCase: true);
        Assert.Equal(world.SightingId, started.Thread.SightingId);
        Assert.Equal(aliceDisplayName, started.Thread.CounterpartDisplayName);
        Assert.False(started.Thread.IsOwner);
        Assert.Equal("I can help catch it", started.Message.Text);
        Assert.Equal(bobDisplayName, started.Message.SenderDisplayName);
        Assert.True(started.Message.IsMine);
    }

    [Fact]
    public async Task A_second_start_by_bob_reuses_his_thread()
    {
        World world = await SeedAsync();

        Guid firstThreadId = await world.Bob.StartThreadAsync(world.SightingId, "I can help catch it", cancellationToken);
        clock.Advance(TimeSpan.FromSeconds(1));
        Guid secondThreadId = await world.Bob.StartThreadAsync(world.SightingId, "I am on my way", cancellationToken);
        using HttpResponseMessage messages = await world.Bob.Client.GetAsync(MessagesUri(world.SightingId, firstThreadId), cancellationToken);
        MessagePageView page = await ReadMessagePageAsync(messages);
        using HttpResponseMessage aliceThreads = await world.Alice.Client.GetAsync(ThreadsUri(world.SightingId), cancellationToken);
        ThreadPageView threadPage = await ReadThreadPageAsync(aliceThreads);

        Assert.Equal(firstThreadId, secondThreadId);
        Assert.Equal(HttpStatusCode.OK, messages.StatusCode);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(["I am on my way", "I can help catch it"], page.Items.Select(item => item.Text));
        Assert.Equal(1, threadPage.TotalCount);
        Assert.Single(factory.ReadSightingThreadAuditUsers());
    }

    [Fact]
    public async Task Alice_cannot_start_a_thread_on_her_own_sighting()
    {
        World world = await SeedAsync();

        using HttpResponseMessage start = await StartAsync(world.Alice, world.SightingId, new { text = "Talking to myself" });
        string[] codes = await new AccountApiClient(world.Alice.Client).ReadErrorCodesAsync(start, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, start.StatusCode);
        Assert.Equal(["OwnSightingContact"], codes);
        Assert.Empty(factory.ReadSightingThreadAuditUsers());
        Assert.Empty(factory.ReadSightingMessageAuditUsers());
    }

    [Fact]
    public async Task Carol_cannot_read_reply_to_or_list_bobs_thread_and_nothing_is_written()
    {
        World world = await SeedAsync();
        Guid threadId = await world.Bob.StartThreadAsync(world.SightingId, "I can help catch it", cancellationToken);

        using HttpResponseMessage read = await world.Carol.Client.GetAsync(MessagesUri(world.SightingId, threadId), cancellationToken);
        using HttpResponseMessage reply = await ReplyAsync(world.Carol, world.SightingId, threadId, new { text = "Let me in" });
        using HttpResponseMessage sightingThreads = await world.Carol.Client.GetAsync(ThreadsUri(world.SightingId), cancellationToken);
        ThreadPageView sightingThreadPage = await ReadThreadPageAsync(sightingThreads);
        using HttpResponseMessage inbox = await world.Carol.Client.GetAsync("/threads", cancellationToken);
        ThreadPageView inboxPage = await ReadThreadPageAsync(inbox);
        using HttpResponseMessage bobRead = await world.Bob.Client.GetAsync(MessagesUri(world.SightingId, threadId), cancellationToken);
        MessagePageView bobPage = await ReadMessagePageAsync(bobRead);

        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, reply.StatusCode);
        Assert.Equal(HttpStatusCode.OK, sightingThreads.StatusCode);
        Assert.Empty(sightingThreadPage.Items);
        Assert.Equal(0, sightingThreadPage.TotalCount);
        Assert.Equal(HttpStatusCode.OK, inbox.StatusCode);
        Assert.Empty(inboxPage.Items);
        Assert.Equal(0, inboxPage.TotalCount);
        Assert.Equal(HttpStatusCode.OK, bobRead.StatusCode);
        Assert.Equal(1, bobPage.TotalCount);
        Assert.Equal(["I can help catch it"], bobPage.Items.Select(item => item.Text));
        Assert.Single(factory.ReadSightingMessageAuditUsers());
    }

    [Fact]
    public async Task Forged_sender_participant_owner_and_email_fields_are_ignored()
    {
        World world = await SeedAsync();

        using HttpResponseMessage start = await StartAsync(
            world.Bob,
            world.SightingId,
            new
            {
                text = "I can help catch it",
                senderId = world.Carol.Id,
                participantId = world.Carol.Id,
                ownerId = world.Bob.Id,
                email = carolEmail,
                userId = world.Carol.Id
            });
        StartView started = await ReadStartAsync(start);
        clock.Advance(TimeSpan.FromSeconds(1));
        using HttpResponseMessage reply = await ReplyAsync(
            world.Alice,
            world.SightingId,
            started.Thread.Id,
            new
            {
                text = "Thank you",
                senderId = world.Bob.Id,
                participantId = world.Carol.Id,
                ownerId = world.Bob.Id,
                email = bobEmail,
                userId = world.Bob.Id
            });
        MessageView replied = await ReadMessageAsync(reply);
        using HttpResponseMessage aliceThreads = await world.Alice.Client.GetAsync(ThreadsUri(world.SightingId), cancellationToken);
        ThreadPageView aliceThreadPage = await ReadThreadPageAsync(aliceThreads);
        using HttpResponseMessage bobThreads = await world.Bob.Client.GetAsync(ThreadsUri(world.SightingId), cancellationToken);
        ThreadPageView bobThreadPage = await ReadThreadPageAsync(bobThreads);
        using HttpResponseMessage carolInbox = await world.Carol.Client.GetAsync("/threads", cancellationToken);
        ThreadPageView carolInboxPage = await ReadThreadPageAsync(carolInbox);
        using HttpResponseMessage aliceMessages = await world.Alice.Client.GetAsync(MessagesUri(world.SightingId, started.Thread.Id), cancellationToken);
        MessagePageView aliceMessagePage = await ReadMessagePageAsync(aliceMessages);
        using HttpResponseMessage bobMessages = await world.Bob.Client.GetAsync(MessagesUri(world.SightingId, started.Thread.Id), cancellationToken);
        MessagePageView bobMessagePage = await ReadMessagePageAsync(bobMessages);

        Assert.Equal(HttpStatusCode.Created, start.StatusCode);
        Assert.Equal(HttpStatusCode.Created, reply.StatusCode);
        Assert.Equal(aliceDisplayName, replied.SenderDisplayName);
        Assert.True(replied.IsMine);
        ThreadView aliceThread = Assert.Single(aliceThreadPage.Items);
        Assert.Equal(started.Thread.Id, aliceThread.Id);
        Assert.Equal(bobDisplayName, aliceThread.CounterpartDisplayName);
        Assert.True(aliceThread.IsOwner);
        ThreadView bobThread = Assert.Single(bobThreadPage.Items);
        Assert.Equal(started.Thread.Id, bobThread.Id);
        Assert.Equal(aliceDisplayName, bobThread.CounterpartDisplayName);
        Assert.False(bobThread.IsOwner);
        Assert.Equal(0, carolInboxPage.TotalCount);
        Assert.Equal(
            [new MessageView(replied.Id, "Thank you", aliceDisplayName, true), new MessageView(started.Message.Id, "I can help catch it", bobDisplayName, false)],
            aliceMessagePage.Items);
        Assert.Equal(
            [new MessageView(replied.Id, "Thank you", aliceDisplayName, false), new MessageView(started.Message.Id, "I can help catch it", bobDisplayName, true)],
            bobMessagePage.Items);
    }

    [Fact]
    public async Task Unknown_sighting_or_thread_is_a_404()
    {
        World world = await SeedAsync();
        Guid threadId = await world.Bob.StartThreadAsync(world.SightingId, "I can help catch it", cancellationToken);
        Guid unknownSighting = Guid.NewGuid();

        using HttpResponseMessage start = await StartAsync(world.Bob, unknownSighting, new { text = "Hello" });
        using HttpResponseMessage list = await world.Bob.Client.GetAsync(ThreadsUri(unknownSighting), cancellationToken);
        using HttpResponseMessage read = await world.Bob.Client.GetAsync(MessagesUri(unknownSighting, threadId), cancellationToken);
        using HttpResponseMessage reply = await ReplyAsync(world.Bob, unknownSighting, threadId, new { text = "Hello" });
        using HttpResponseMessage unknownThreadRead = await world.Bob.Client.GetAsync(MessagesUri(world.SightingId, Guid.NewGuid()), cancellationToken);
        using HttpResponseMessage unknownThreadReply = await ReplyAsync(world.Bob, world.SightingId, Guid.NewGuid(), new { text = "Hello" });

        Assert.Equal(HttpStatusCode.NotFound, start.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, list.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, reply.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownThreadRead.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownThreadReply.StatusCode);
        Assert.Single(factory.ReadSightingThreadAuditUsers());
        Assert.Single(factory.ReadSightingMessageAuditUsers());
    }

    [Fact]
    public async Task Whitespace_text_is_message_text_required_and_1001_characters_is_a_400_while_1000_is_accepted()
    {
        World world = await SeedAsync();

        using HttpResponseMessage whitespace = await StartAsync(world.Bob, world.SightingId, new { text = "   \n\t " });
        string[] codes = await new AccountApiClient(world.Bob.Client).ReadErrorCodesAsync(whitespace, cancellationToken);
        using HttpResponseMessage tooLong = await StartAsync(world.Bob, world.SightingId, new { text = new string('x', 1001) });
        int messagesAfterRejections = factory.ReadSightingMessageAuditUsers().Count;
        using HttpResponseMessage atLimit = await StartAsync(world.Bob, world.SightingId, new { text = new string('x', 1000) });
        StartView started = await ReadStartAsync(atLimit);

        Assert.Equal(HttpStatusCode.BadRequest, whitespace.StatusCode);
        Assert.Equal(["MessageTextRequired"], codes);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal(0, messagesAfterRejections);
        Assert.Equal(HttpStatusCode.Created, atLimit.StatusCode);
        Assert.Equal(1000, started.Message.Text.Length);
    }

    [Fact]
    public async Task Messages_are_newest_first_in_pages_of_20_and_page_0_is_invalid_page()
    {
        World world = await SeedAsync();
        Guid threadId = await world.Bob.StartThreadAsync(world.SightingId, "Message 0", cancellationToken);
        List<string> sent = ["Message 0"];

        for (int index = 1; index < 21; index++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            TestUser sender = index % 2 == 0 ? world.Bob : world.Alice;
            await sender.ReplyAsync(world.SightingId, threadId, $"Message {index}", cancellationToken);
            sent.Add($"Message {index}");
        }

        sent.Reverse();

        using HttpResponseMessage first = await world.Alice.Client.GetAsync(MessagesUri(world.SightingId, threadId), cancellationToken);
        MessagePageView firstPage = await ReadMessagePageAsync(first);
        using HttpResponseMessage second = await world.Alice.Client.GetAsync($"{MessagesUri(world.SightingId, threadId)}?page=2", cancellationToken);
        MessagePageView secondPage = await ReadMessagePageAsync(second);
        using HttpResponseMessage zero = await world.Alice.Client.GetAsync($"{MessagesUri(world.SightingId, threadId)}?page=0", cancellationToken);
        string[] zeroCodes = await new AccountApiClient(world.Alice.Client).ReadErrorCodesAsync(zero, cancellationToken);
        using HttpResponseMessage threads = await world.Alice.Client.GetAsync(ThreadsUri(world.SightingId), cancellationToken);
        using HttpResponseMessage inbox = await world.Alice.Client.GetAsync("/threads", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("no-store, private", NormalizedCacheControl(first));
        Assert.Equal(sent.Take(20), firstPage.Items.Select(item => item.Text));
        Assert.Equal(21, firstPage.TotalCount);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(sent.Skip(20), secondPage.Items.Select(item => item.Text));
        Assert.Equal(21, secondPage.TotalCount);
        Assert.Equal(HttpStatusCode.BadRequest, zero.StatusCode);
        Assert.Equal(["InvalidPage"], zeroCodes);
        Assert.Equal("no-store, private", NormalizedCacheControl(threads));
        Assert.Equal("no-store, private", NormalizedCacheControl(inbox));
    }

    [Fact]
    public async Task Thread_lists_show_the_counterpart_and_the_owner_flag_for_each_side()
    {
        World world = await SeedAsync();
        Guid threadId = await world.Bob.StartThreadAsync(world.SightingId, "I can help catch it", cancellationToken);

        using HttpResponseMessage aliceInbox = await world.Alice.Client.GetAsync("/threads", cancellationToken);
        ThreadPageView aliceInboxPage = await ReadThreadPageAsync(aliceInbox);
        using HttpResponseMessage bobInbox = await world.Bob.Client.GetAsync("/threads", cancellationToken);
        ThreadPageView bobInboxPage = await ReadThreadPageAsync(bobInbox);
        using HttpResponseMessage aliceSightingThreads = await world.Alice.Client.GetAsync(ThreadsUri(world.SightingId), cancellationToken);
        ThreadPageView aliceSightingPage = await ReadThreadPageAsync(aliceSightingThreads);
        using HttpResponseMessage bobSightingThreads = await world.Bob.Client.GetAsync(ThreadsUri(world.SightingId), cancellationToken);
        ThreadPageView bobSightingPage = await ReadThreadPageAsync(bobSightingThreads);

        foreach (ThreadPageView page in new[] { aliceInboxPage, aliceSightingPage })
        {
            ThreadView thread = Assert.Single(page.Items);
            Assert.Equal(threadId, thread.Id);
            Assert.Equal(world.SightingId, thread.SightingId);
            Assert.Equal(bobDisplayName, thread.CounterpartDisplayName);
            Assert.True(thread.IsOwner);
        }

        foreach (ThreadPageView page in new[] { bobInboxPage, bobSightingPage })
        {
            ThreadView thread = Assert.Single(page.Items);
            Assert.Equal(threadId, thread.Id);
            Assert.Equal(world.SightingId, thread.SightingId);
            Assert.Equal(aliceDisplayName, thread.CounterpartDisplayName);
            Assert.False(thread.IsOwner);
        }
    }

    [Fact]
    public async Task Sighting_is_mine_for_alice_and_not_for_bob()
    {
        World world = await SeedAsync();

        using HttpResponseMessage aliceRead = await world.Alice.Client.GetAsync($"/sightings/{world.SightingId}", cancellationToken);
        bool aliceIsMine = await ReadIsMineAsync(aliceRead);
        using HttpResponseMessage bobRead = await world.Bob.Client.GetAsync($"/sightings/{world.SightingId}", cancellationToken);
        bool bobIsMine = await ReadIsMineAsync(bobRead);

        Assert.Equal(HttpStatusCode.OK, aliceRead.StatusCode);
        Assert.True(aliceIsMine);
        Assert.Equal(HttpStatusCode.OK, bobRead.StatusCode);
        Assert.False(bobIsMine);
    }

    [Fact]
    public async Task Thread_message_and_sighting_responses_carry_no_user_id_audit_user_or_email()
    {
        World world = await SeedAsync();
        using HttpResponseMessage start = await StartAsync(world.Bob, world.SightingId, new { text = "I can help catch it" });
        StartView started = await ReadStartAsync(start);
        clock.Advance(TimeSpan.FromSeconds(1));
        using HttpResponseMessage secondStart = await StartAsync(world.Bob, world.SightingId, new { text = "I am on my way" });
        clock.Advance(TimeSpan.FromSeconds(1));
        using HttpResponseMessage reply = await ReplyAsync(world.Alice, world.SightingId, started.Thread.Id, new { text = "Thank you" });
        List<HttpResponseMessage> reads =
        [
            await world.Alice.Client.GetAsync(MessagesUri(world.SightingId, started.Thread.Id), cancellationToken),
            await world.Bob.Client.GetAsync(MessagesUri(world.SightingId, started.Thread.Id), cancellationToken),
            await world.Alice.Client.GetAsync(ThreadsUri(world.SightingId), cancellationToken),
            await world.Bob.Client.GetAsync(ThreadsUri(world.SightingId), cancellationToken),
            await world.Alice.Client.GetAsync("/threads", cancellationToken),
            await world.Bob.Client.GetAsync("/threads", cancellationToken),
            await world.Alice.Client.GetAsync("/threads/unread-count", cancellationToken),
            await world.Bob.Client.GetAsync("/threads/unread-count", cancellationToken),
            await world.Alice.Client.GetAsync($"/sightings/{world.SightingId}", cancellationToken),
            await world.Bob.Client.GetAsync($"/sightings/{world.SightingId}", cancellationToken)
        ];
        List<string> bodies =
        [
            await start.Content.ReadAsStringAsync(cancellationToken),
            await secondStart.Content.ReadAsStringAsync(cancellationToken),
            await reply.Content.ReadAsStringAsync(cancellationToken)
        ];

        foreach (HttpResponseMessage read in reads)
            bodies.Add(await read.Content.ReadAsStringAsync(cancellationToken));

        string[] forbidden =
        [
            "senderId",
            "participantId",
            "ownerId",
            "reporterId",
            "createUser",
            "updateUser",
            "email",
            world.Alice.Id.ToString("D"),
            world.Alice.Id.ToString("N"),
            world.Bob.Id.ToString("D"),
            world.Bob.Id.ToString("N"),
            aliceEmail,
            bobEmail
        ];

        Assert.Equal(HttpStatusCode.Created, start.StatusCode);
        Assert.Equal(HttpStatusCode.Created, secondStart.StatusCode);
        Assert.Equal(HttpStatusCode.Created, reply.StatusCode);
        Assert.All(reads, read => Assert.Equal(HttpStatusCode.OK, read.StatusCode));

        foreach (string body in bodies)
        {
            foreach (string needle in forbidden)
                Assert.DoesNotContain(needle, body, StringComparison.OrdinalIgnoreCase);
        }

        foreach (HttpResponseMessage read in reads)
            read.Dispose();
    }

    [Fact]
    public async Task Bobs_messages_are_unread_for_alice_until_she_marks_the_thread_read()
    {
        World world = await SeedAsync();
        Guid threadId = await world.Bob.StartThreadAsync(world.SightingId, "I can help catch it", cancellationToken);
        clock.Advance(TimeSpan.FromSeconds(1));
        await world.Bob.StartThreadAsync(world.SightingId, "I am on my way", cancellationToken);
        clock.Advance(TimeSpan.FromSeconds(1));

        ThreadView aliceBefore = Assert.Single((await GetInboxAsync(world.Alice)).Items);
        ThreadView aliceSightingBefore = Assert.Single((await GetSightingThreadsAsync(world.Alice, world.SightingId)).Items);
        ThreadView bobBefore = Assert.Single((await GetInboxAsync(world.Bob)).Items);
        int aliceCountBefore = await GetUnreadCountAsync(world.Alice);
        int bobCountBefore = await GetUnreadCountAsync(world.Bob);

        using HttpResponseMessage markRead = await MarkReadAsync(world.Alice, world.SightingId, threadId);
        string markReadBody = await markRead.Content.ReadAsStringAsync(cancellationToken);

        ThreadView aliceAfter = Assert.Single((await GetInboxAsync(world.Alice)).Items);
        int aliceCountAfter = await GetUnreadCountAsync(world.Alice);

        clock.Advance(TimeSpan.FromSeconds(1));
        await world.Alice.ReplyAsync(world.SightingId, threadId, "Thank you", cancellationToken);

        ThreadView aliceAfterReply = Assert.Single((await GetInboxAsync(world.Alice)).Items);
        ThreadView bobAfterReply = Assert.Single((await GetInboxAsync(world.Bob)).Items);
        int bobCountAfterReply = await GetUnreadCountAsync(world.Bob);

        clock.Advance(TimeSpan.FromSeconds(1));
        await world.Bob.ReplyAsync(world.SightingId, threadId, "See you soon", cancellationToken);

        int aliceCountAfterBobFollowUp = await GetUnreadCountAsync(world.Alice);
        int bobCountAfterOwnFollowUp = await GetUnreadCountAsync(world.Bob);

        Assert.True(aliceBefore.HasUnread);
        Assert.True(aliceSightingBefore.HasUnread);
        Assert.False(bobBefore.HasUnread);
        Assert.Equal(2, aliceCountBefore);
        Assert.Equal(0, bobCountBefore);
        Assert.Equal(HttpStatusCode.NoContent, markRead.StatusCode);
        Assert.Empty(markReadBody);
        Assert.False(aliceAfter.HasUnread);
        Assert.Equal(0, aliceCountAfter);
        Assert.False(aliceAfterReply.HasUnread);
        Assert.True(bobAfterReply.HasUnread);
        Assert.Equal(1, bobCountAfterReply);
        Assert.Equal(1, aliceCountAfterBobFollowUp);
        Assert.Equal(0, bobCountAfterOwnFollowUp);
    }

    [Fact]
    public async Task Carol_cannot_mark_bobs_thread_read_and_nothing_is_written()
    {
        World world = await SeedAsync();
        Guid threadId = await world.Bob.StartThreadAsync(world.SightingId, "I can help catch it", cancellationToken);
        IReadOnlyList<AuditUsers> auditBefore = factory.ReadSightingThreadAuditUsers();
        clock.Advance(TimeSpan.FromSeconds(1));

        using HttpResponseMessage carolMark = await MarkReadAsync(world.Carol, world.SightingId, threadId);
        using HttpResponseMessage unknownThread = await MarkReadAsync(world.Alice, world.SightingId, Guid.NewGuid());
        using HttpResponseMessage unknownSighting = await MarkReadAsync(world.Alice, Guid.NewGuid(), threadId);
        ThreadView aliceThread = Assert.Single((await GetInboxAsync(world.Alice)).Items);

        Assert.Equal(HttpStatusCode.NotFound, carolMark.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownThread.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownSighting.StatusCode);
        Assert.Equal(auditBefore, factory.ReadSightingThreadAuditUsers());
        Assert.True(aliceThread.HasUnread);
        Assert.Equal(1, await GetUnreadCountAsync(world.Alice));
    }

    [Fact]
    public async Task The_unread_count_counts_messages_and_never_includes_another_users_threads()
    {
        World world = await SeedAsync();
        Guid carolSightingId = await world.Carol.CreateSightingAsync(cancellationToken: cancellationToken);
        await world.Bob.StartThreadAsync(world.SightingId, "To Alice", cancellationToken);
        await world.Bob.StartThreadAsync(carolSightingId, "To Carol", cancellationToken);
        clock.Advance(TimeSpan.FromSeconds(1));
        await world.Bob.StartThreadAsync(carolSightingId, "To Carol again", cancellationToken);
        await world.Alice.StartThreadAsync(carolSightingId, "Alice to Carol", cancellationToken);

        using HttpResponseMessage carolCount = await world.Carol.Client.GetAsync("/threads/unread-count", cancellationToken);
        int carolUnread = await ReadCountAsync(carolCount);

        Assert.Equal(HttpStatusCode.OK, carolCount.StatusCode);
        Assert.Equal("no-store, private", NormalizedCacheControl(carolCount));
        Assert.Equal(3, carolUnread);
        Assert.Equal(1, await GetUnreadCountAsync(world.Alice));
        Assert.Equal(0, await GetUnreadCountAsync(world.Bob));
    }

    public void Dispose()
    {
        foreach (TestUser user in users)
            user.Dispose();

        factory.Dispose();
    }

    private static string ThreadsUri(Guid sightingId)
    {
        return $"/sightings/{sightingId}/threads";
    }

    private static string MessagesUri(Guid sightingId, Guid threadId)
    {
        return $"/sightings/{sightingId}/threads/{threadId}/messages";
    }

    private static string NormalizedCacheControl(HttpResponseMessage response)
    {
        return string.Join(", ", response.Headers.CacheControl!.ToString().Split(',').Select(part => part.Trim()).Order(StringComparer.Ordinal));
    }

    private async Task<World> SeedAsync()
    {
        TestUser alice = await SeedUserAsync(aliceEmail, aliceDisplayName);
        TestUser bob = await SeedUserAsync(bobEmail, bobDisplayName);
        TestUser carol = await SeedUserAsync(carolEmail, carolDisplayName);
        Guid sightingId = await alice.CreateSightingAsync(cancellationToken: cancellationToken);

        return new World(alice, bob, carol, sightingId);
    }

    private async Task<TestUser> SeedUserAsync(string email, string displayName)
    {
        TestUser user = await factory.CreateUserAsync(email, password, displayName, cancellationToken);
        users.Add(user);

        return user;
    }

    private async Task<HttpResponseMessage> StartAsync(TestUser user, Guid sightingId, object body)
    {
        HttpResponseMessage response = await user.Client.PostAsJsonAsync(ThreadsUri(sightingId), body, cancellationToken);

        return response;
    }

    private async Task<HttpResponseMessage> ReplyAsync(TestUser user, Guid sightingId, Guid threadId, object body)
    {
        HttpResponseMessage response = await user.Client.PostAsJsonAsync(MessagesUri(sightingId, threadId), body, cancellationToken);

        return response;
    }

    private async Task<HttpResponseMessage> MarkReadAsync(TestUser user, Guid sightingId, Guid threadId)
    {
        HttpResponseMessage response = await user.Client.PutAsync($"{ThreadsUri(sightingId)}/{threadId}/read", null, cancellationToken);

        return response;
    }

    private async Task<ThreadPageView> GetInboxAsync(TestUser user)
    {
        using HttpResponseMessage response = await user.Client.GetAsync("/threads", cancellationToken);

        return await ReadThreadPageAsync(response);
    }

    private async Task<ThreadPageView> GetSightingThreadsAsync(TestUser user, Guid sightingId)
    {
        using HttpResponseMessage response = await user.Client.GetAsync(ThreadsUri(sightingId), cancellationToken);

        return await ReadThreadPageAsync(response);
    }

    private async Task<int> GetUnreadCountAsync(TestUser user)
    {
        using HttpResponseMessage response = await user.Client.GetAsync("/threads/unread-count", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await ReadCountAsync(response);
    }

    private async Task<int> ReadCountAsync(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        using JsonDocument document = JsonDocument.Parse(body);

        return document.RootElement.GetProperty("count").GetInt32();
    }

    private async Task<bool> ReadIsMineAsync(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        using JsonDocument document = JsonDocument.Parse(body);

        return document.RootElement.GetProperty("isMine").GetBoolean();
    }

    private async Task<StartView> ReadStartAsync(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        using JsonDocument document = JsonDocument.Parse(body);

        return new StartView(ToThread(document.RootElement.GetProperty("thread")), ToMessage(document.RootElement.GetProperty("message")));
    }

    private async Task<MessageView> ReadMessageAsync(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        using JsonDocument document = JsonDocument.Parse(body);

        return ToMessage(document.RootElement);
    }

    private async Task<MessagePageView> ReadMessagePageAsync(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        using JsonDocument document = JsonDocument.Parse(body);
        List<MessageView> items = document.RootElement
            .GetProperty("items")
            .EnumerateArray()
            .Select(ToMessage)
            .ToList();
        int totalCount = document.RootElement.GetProperty("totalCount").GetInt32();

        return new MessagePageView(items, totalCount);
    }

    private async Task<ThreadPageView> ReadThreadPageAsync(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        using JsonDocument document = JsonDocument.Parse(body);
        List<ThreadView> items = document.RootElement
            .GetProperty("items")
            .EnumerateArray()
            .Select(ToThread)
            .ToList();
        int totalCount = document.RootElement.GetProperty("totalCount").GetInt32();

        return new ThreadPageView(items, totalCount);
    }

    private static ThreadView ToThread(JsonElement element)
    {
        return new ThreadView(
            element.GetProperty("id").GetGuid(),
            element.GetProperty("sightingId").GetGuid(),
            element.GetProperty("counterpartDisplayName").GetString()!,
            element.GetProperty("isOwner").GetBoolean(),
            element.GetProperty("hasUnread").GetBoolean());
    }

    private static MessageView ToMessage(JsonElement element)
    {
        return new MessageView(
            element.GetProperty("id").GetGuid(),
            element.GetProperty("text").GetString()!,
            element.GetProperty("senderDisplayName").GetString()!,
            element.GetProperty("isMine").GetBoolean());
    }

    private sealed record World(TestUser Alice, TestUser Bob, TestUser Carol, Guid SightingId);

    private sealed record ThreadView(Guid Id, Guid SightingId, string CounterpartDisplayName, bool IsOwner, bool HasUnread);

    private sealed record MessageView(Guid Id, string Text, string SenderDisplayName, bool IsMine);

    private sealed record StartView(ThreadView Thread, MessageView Message);

    private sealed record ThreadPageView(IReadOnlyList<ThreadView> Items, int TotalCount);

    private sealed record MessagePageView(IReadOnlyList<MessageView> Items, int TotalCount);
}

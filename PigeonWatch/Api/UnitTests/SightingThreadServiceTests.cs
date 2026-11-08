using NSubstitute;
using PigeonWatch.BusinessLogic.Services;
using PigeonWatch.BusinessObjects;
using PigeonWatch.Data.Repositories;

namespace PigeonWatch.UnitTests;

public class SightingThreadServiceTests
{
    private static readonly DateTimeOffset now = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly ISightingThreadRepository repository = Substitute.For<ISightingThreadRepository>();
    private readonly ManualTimeProvider timeProvider = new(now);
    private readonly CancellationToken cancellationToken = TestContext.Current.CancellationToken;
    private readonly Guid sightingId = Guid.NewGuid();
    private readonly Guid ownerId = Guid.NewGuid();
    private readonly Guid participantId = Guid.NewGuid();
    private readonly Guid threadId = Guid.NewGuid();

    public SightingThreadServiceTests()
    {
        repository.GetSightingOwnerAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Guid?)null);
        repository.GetSightingOwnerAsync(sightingId, Arg.Any<CancellationToken>()).Returns(ownerId);
        repository
            .StartOrAppendAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(call => ((SightingThread Thread, SightingMessage Message)?)(Thread(), Message(call.ArgAt<Guid>(1), call.ArgAt<string>(2))));
        repository
            .AppendAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(call => Message(call.ArgAt<Guid>(2), call.ArgAt<string>(3)));
        repository
            .ListThreadsForSightingAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new SightingThreadPage([], 0));
        repository
            .ListThreadsForUserAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new SightingThreadPage([], 0));
        repository
            .ListMessagesAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new SightingMessagePage([], 0));
    }

    [Fact]
    public async Task Start_trims_the_text_and_starts_or_appends_at_the_current_time()
    {
        SightingThreadResult result = await CreateService().StartAsync(sightingId, participantId, "  Is the bird still there? \n", cancellationToken);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Thread);
        Assert.NotNull(result.Message);
        await repository.Received(1).StartOrAppendAsync(sightingId, participantId, "Is the bird still there?", now.UtcDateTime, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Start_rejects_whitespace_only_text_before_the_sighting_lookup()
    {
        SightingThreadResult result = await CreateService().StartAsync(Guid.NewGuid(), participantId, " \t ", cancellationToken);

        Assert.Equal([SightingMessageErrorCodes.MessageTextRequired], Codes(result.Errors));
        await repository.DidNotReceive().GetSightingOwnerAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await AssertNoWriteAsync();
    }

    [Fact]
    public async Task Start_rejects_text_over_the_limit_after_trimming()
    {
        SightingThreadResult tooLong = await CreateService().StartAsync(sightingId, participantId, new string('x', SightingMessageRules.TextMaxLength + 1), cancellationToken);
        SightingThreadResult padded = await CreateService().StartAsync(sightingId, participantId, $" {new string('x', SightingMessageRules.TextMaxLength)} ", cancellationToken);

        Assert.Equal([SightingMessageErrorCodes.MessageTextTooLong], Codes(tooLong.Errors));
        Assert.True(padded.Succeeded);
    }

    [Fact]
    public async Task Start_returns_sighting_not_found_for_an_unknown_sighting()
    {
        SightingThreadResult result = await CreateService().StartAsync(Guid.NewGuid(), participantId, "Hello", cancellationToken);

        Assert.Equal([SightingMessageErrorCodes.SightingNotFound], Codes(result.Errors));
        await AssertNoWriteAsync();
    }

    [Fact]
    public async Task Start_rejects_the_owner_contacting_their_own_sighting()
    {
        SightingThreadResult result = await CreateService().StartAsync(sightingId, ownerId, "Hello", cancellationToken);

        Assert.Equal([SightingMessageErrorCodes.OwnSightingContact], Codes(result.Errors));
        await AssertNoWriteAsync();
    }

    [Fact]
    public async Task Start_returns_sender_not_found_when_the_write_finds_no_user_row()
    {
        repository
            .StartOrAppendAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(((SightingThread Thread, SightingMessage Message)?)null);

        SightingThreadResult result = await CreateService().StartAsync(sightingId, participantId, "Hello", cancellationToken);

        Assert.Equal([SightingMessageErrorCodes.SenderNotFound], Codes(result.Errors));
    }

    [Fact]
    public async Task Start_returns_sighting_not_found_when_the_sighting_is_gone_by_the_time_of_the_write()
    {
        repository.GetSightingOwnerAsync(sightingId, Arg.Any<CancellationToken>()).Returns(ownerId, (Guid?)null);
        repository
            .StartOrAppendAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(((SightingThread Thread, SightingMessage Message)?)null);

        SightingThreadResult result = await CreateService().StartAsync(sightingId, participantId, "Hello", cancellationToken);

        Assert.Equal([SightingMessageErrorCodes.SightingNotFound], Codes(result.Errors));
    }

    [Fact]
    public async Task Reply_appends_the_trimmed_text_for_a_participant()
    {
        StoreThreadFor(participantId);

        SightingThreadResult result = await CreateService().ReplyAsync(sightingId, threadId, participantId, " Thanks ", cancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(now.UtcDateTime, result.Thread!.LastMessageAt);
        await repository.Received(1).AppendAsync(sightingId, threadId, participantId, "Thanks", now.UtcDateTime, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reply_allows_the_owner()
    {
        StoreThreadFor(ownerId);

        SightingThreadResult result = await CreateService().ReplyAsync(sightingId, threadId, ownerId, "On my way", cancellationToken);

        Assert.True(result.Succeeded);
        await repository.Received(1).AppendAsync(sightingId, threadId, ownerId, "On my way", now.UtcDateTime, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reply_validates_the_text_before_any_lookup()
    {
        StoreThreadFor(participantId);

        SightingThreadResult result = await CreateService().ReplyAsync(sightingId, threadId, participantId, "   ", cancellationToken);

        Assert.Equal([SightingMessageErrorCodes.MessageTextRequired], Codes(result.Errors));
        await repository.DidNotReceive().GetSightingOwnerAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await repository.DidNotReceive().GetThreadForUserAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await AssertNoWriteAsync();
    }

    [Fact]
    public async Task Reply_checks_the_sighting_before_the_thread()
    {
        SightingThreadResult result = await CreateService().ReplyAsync(Guid.NewGuid(), threadId, participantId, "Hello", cancellationToken);

        Assert.Equal([SightingMessageErrorCodes.SightingNotFound], Codes(result.Errors));
        await repository.DidNotReceive().GetThreadForUserAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await AssertNoWriteAsync();
    }

    [Fact]
    public async Task Reply_returns_thread_not_found_for_an_outsider()
    {
        StoreThreadFor(participantId);

        SightingThreadResult result = await CreateService().ReplyAsync(sightingId, threadId, Guid.NewGuid(), "Hello", cancellationToken);

        Assert.Equal([SightingMessageErrorCodes.ThreadNotFound], Codes(result.Errors));
        await AssertNoWriteAsync();
    }

    [Fact]
    public async Task Reply_returns_sender_not_found_when_the_write_returns_null_after_the_checks()
    {
        StoreThreadFor(participantId);
        repository
            .AppendAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns((SightingMessage?)null);

        SightingThreadResult result = await CreateService().ReplyAsync(sightingId, threadId, participantId, "Hello", cancellationToken);

        Assert.Equal([SightingMessageErrorCodes.SenderNotFound], Codes(result.Errors));
    }

    [Fact]
    public async Task List_for_sighting_returns_the_page_of_20_for_the_caller()
    {
        SightingThreadPage stored = new([Thread()], 21);
        repository.ListThreadsForSightingAsync(sightingId, ownerId, 2, SightingMessageRules.PageSize, Arg.Any<CancellationToken>()).Returns(stored);

        SightingThreadPageResult result = await CreateService().ListForSightingAsync(sightingId, ownerId, 2, cancellationToken);

        Assert.True(result.Succeeded);
        Assert.Same(stored, result.Page);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(int.MaxValue)]
    public async Task List_for_sighting_rejects_an_invalid_page_before_the_sighting_lookup(int page)
    {
        SightingThreadPageResult result = await CreateService().ListForSightingAsync(sightingId, ownerId, page, cancellationToken);

        Assert.Equal([SightingMessageErrorCodes.InvalidPage], Codes(result.Errors));
        await repository.DidNotReceive().GetSightingOwnerAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task List_for_sighting_returns_sighting_not_found_for_an_unknown_sighting()
    {
        SightingThreadPageResult result = await CreateService().ListForSightingAsync(Guid.NewGuid(), ownerId, 1, cancellationToken);

        Assert.Equal([SightingMessageErrorCodes.SightingNotFound], Codes(result.Errors));
        await repository.DidNotReceive().ListThreadsForSightingAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task List_for_user_returns_the_callers_threads()
    {
        SightingThreadPage stored = new([Thread()], 1);
        repository.ListThreadsForUserAsync(participantId, 1, SightingMessageRules.PageSize, Arg.Any<CancellationToken>()).Returns(stored);

        SightingThreadPageResult result = await CreateService().ListForUserAsync(participantId, 1, cancellationToken);

        Assert.True(result.Succeeded);
        Assert.Same(stored, result.Page);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public async Task List_for_user_rejects_an_invalid_page(int page)
    {
        SightingThreadPageResult result = await CreateService().ListForUserAsync(participantId, page, cancellationToken);

        Assert.Equal([SightingMessageErrorCodes.InvalidPage], Codes(result.Errors));
        await repository.DidNotReceive().ListThreadsForUserAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task List_messages_returns_the_page_for_a_participant()
    {
        StoreThreadFor(participantId);
        SightingMessagePage stored = new([Message(participantId, "Hello")], 1);
        repository.ListMessagesAsync(sightingId, threadId, participantId, 1, SightingMessageRules.PageSize, Arg.Any<CancellationToken>()).Returns(stored);

        SightingMessagePageResult result = await CreateService().ListMessagesAsync(sightingId, threadId, participantId, 1, cancellationToken);

        Assert.True(result.Succeeded);
        Assert.Same(stored, result.Page);
    }

    [Fact]
    public async Task List_messages_rejects_an_invalid_page_before_any_lookup()
    {
        SightingMessagePageResult result = await CreateService().ListMessagesAsync(sightingId, threadId, participantId, 0, cancellationToken);

        Assert.Equal([SightingMessageErrorCodes.InvalidPage], Codes(result.Errors));
        await repository.DidNotReceive().GetSightingOwnerAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task List_messages_returns_sighting_not_found_for_an_unknown_sighting()
    {
        SightingMessagePageResult result = await CreateService().ListMessagesAsync(Guid.NewGuid(), threadId, participantId, 1, cancellationToken);

        Assert.Equal([SightingMessageErrorCodes.SightingNotFound], Codes(result.Errors));
        await repository.DidNotReceive().IsThreadVisibleToUserAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task List_messages_checks_access_without_loading_the_thread()
    {
        StoreThreadFor(participantId);

        SightingMessagePageResult result = await CreateService().ListMessagesAsync(sightingId, threadId, participantId, 1, cancellationToken);

        Assert.True(result.Succeeded);
        await repository.DidNotReceive().GetThreadForUserAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task List_messages_returns_thread_not_found_for_an_outsider_without_reading_messages()
    {
        StoreThreadFor(participantId);

        SightingMessagePageResult result = await CreateService().ListMessagesAsync(sightingId, threadId, Guid.NewGuid(), 1, cancellationToken);

        Assert.Equal([SightingMessageErrorCodes.ThreadNotFound], Codes(result.Errors));
        await repository.DidNotReceive().ListMessagesAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reply_returns_the_thread_as_read_for_the_sender()
    {
        repository.GetThreadForUserAsync(sightingId, threadId, ownerId, Arg.Any<CancellationToken>()).Returns(Thread() with { HasUnread = true });

        SightingThreadResult result = await CreateService().ReplyAsync(sightingId, threadId, ownerId, "Thanks", cancellationToken);

        Assert.True(result.Succeeded);
        Assert.False(result.Thread!.HasUnread);
    }

    [Fact]
    public async Task Mark_read_passes_the_seen_time_and_the_current_time_to_the_repository()
    {
        DateTime seenAt = now.UtcDateTime.AddMinutes(-1);
        repository.MarkReadAsync(sightingId, threadId, ownerId, seenAt, now.UtcDateTime, Arg.Any<CancellationToken>()).Returns(true);

        SightingThreadReadResult result = await CreateService().MarkReadAsync(sightingId, threadId, ownerId, seenAt, cancellationToken);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Errors);
        await repository.Received(1).MarkReadAsync(sightingId, threadId, ownerId, seenAt, now.UtcDateTime, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Mark_read_returns_sighting_not_found_before_touching_the_thread()
    {
        SightingThreadReadResult result = await CreateService().MarkReadAsync(Guid.NewGuid(), threadId, ownerId, now.UtcDateTime, cancellationToken);

        Assert.Equal([SightingMessageErrorCodes.SightingNotFound], Codes(result.Errors));
        await repository.DidNotReceive().MarkReadAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Mark_read_returns_thread_not_found_for_an_outsider_or_an_unknown_thread()
    {
        repository.MarkReadAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(false);

        SightingThreadReadResult result = await CreateService().MarkReadAsync(sightingId, threadId, Guid.NewGuid(), now.UtcDateTime, cancellationToken);

        Assert.Equal([SightingMessageErrorCodes.ThreadNotFound], Codes(result.Errors));
    }

    [Fact]
    public async Task Count_unread_returns_the_repository_count_for_the_caller()
    {
        repository.CountUnreadMessagesAsync(participantId, Arg.Any<CancellationToken>()).Returns(3);

        int count = await CreateService().CountUnreadMessagesAsync(participantId, cancellationToken);

        Assert.Equal(3, count);
    }

    private SightingThreadService CreateService()
    {
        return new(repository, timeProvider);
    }

    private void StoreThreadFor(Guid userId)
    {
        repository.GetThreadForUserAsync(sightingId, threadId, userId, Arg.Any<CancellationToken>()).Returns(Thread());
        repository.IsThreadVisibleToUserAsync(sightingId, threadId, userId, Arg.Any<CancellationToken>()).Returns(true);
    }

    private SightingThread Thread()
    {
        return new(threadId, sightingId, "Grey", ownerId, "Alice", participantId, "Bob", now.UtcDateTime.AddMinutes(-10), false);
    }

    private SightingMessage Message(Guid senderId, string text)
    {
        return new(Guid.NewGuid(), threadId, senderId, "Bob", text, now.UtcDateTime);
    }

    private static IEnumerable<string> Codes(IReadOnlyList<SightingError> errors)
    {
        return errors.Select(error => error.Code);
    }

    private async Task AssertNoWriteAsync()
    {
        await repository.DidNotReceive().StartOrAppendAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await repository.DidNotReceive().AppendAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }
}

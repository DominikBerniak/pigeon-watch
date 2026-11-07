using NSubstitute;
using PigeonWatch.BusinessLogic.Services;
using PigeonWatch.BusinessObjects;
using PigeonWatch.Data.Repositories;

namespace PigeonWatch.UnitTests;

public class SightingCommentServiceTests
{
    private static readonly DateTimeOffset now = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly ISightingCommentRepository repository = Substitute.For<ISightingCommentRepository>();
    private readonly ManualTimeProvider timeProvider = new(now);
    private readonly CancellationToken cancellationToken = TestContext.Current.CancellationToken;
    private readonly Guid sightingId = Guid.NewGuid();
    private readonly Guid authorId = Guid.NewGuid();

    public SightingCommentServiceTests()
    {
        repository.SightingExistsAsync(sightingId, Arg.Any<CancellationToken>()).Returns(true);
        repository
            .GetLatestSubmissionDateByAuthorAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((DateTime?)null);
        repository
            .CreateAsync(Arg.Any<NewSightingComment>(), Arg.Any<CancellationToken>())
            .Returns(call => Comment(call.Arg<NewSightingComment>().AuthorId, call.Arg<NewSightingComment>().Text));
        repository
            .ListPageAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new SightingCommentPage([], 0));
    }

    [Fact]
    public async Task List_returns_the_requested_page_of_20()
    {
        SightingCommentPage stored = new([Comment(authorId, "Hello")], 21);
        repository.ListPageAsync(sightingId, 2, SightingCommentRules.PageSize, Arg.Any<CancellationToken>()).Returns(stored);

        SightingCommentPageResult result = await CreateService().ListAsync(sightingId, 2, cancellationToken);

        Assert.True(result.Succeeded);
        Assert.Same(stored, result.Page);
    }

    [Fact]
    public async Task List_rejects_page_0_before_looking_up_the_sighting()
    {
        SightingCommentPageResult result = await CreateService().ListAsync(sightingId, 0, cancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal([SightingCommentErrorCodes.InvalidPage], result.Errors.Select(error => error.Code));
        await repository.DidNotReceive().SightingExistsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task List_rejects_a_page_that_would_overflow_the_offset()
    {
        SightingCommentPageResult result = await CreateService().ListAsync(sightingId, int.MaxValue, cancellationToken);

        Assert.Equal([SightingCommentErrorCodes.InvalidPage], result.Errors.Select(error => error.Code));
    }

    [Fact]
    public async Task List_returns_sighting_not_found_for_an_unknown_sighting()
    {
        SightingCommentPageResult result = await CreateService().ListAsync(Guid.NewGuid(), 1, cancellationToken);

        Assert.Equal([SightingCommentErrorCodes.SightingNotFound], result.Errors.Select(error => error.Code));
        await repository.DidNotReceive().ListPageAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_trims_the_text_and_inserts_the_comment()
    {
        SightingCommentResult result = await CreateService().CreateAsync(sightingId, authorId, "  Seen near the fountain \n", cancellationToken);

        Assert.True(result.Succeeded);
        await repository.Received(1).CreateAsync(new NewSightingComment(sightingId, authorId, "Seen near the fountain"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_rejects_whitespace_only_text_before_the_sighting_lookup()
    {
        SightingCommentResult result = await CreateService().CreateAsync(Guid.NewGuid(), authorId, "  \t ", cancellationToken);

        Assert.Equal([SightingCommentErrorCodes.CommentTextRequired], result.Errors.Select(error => error.Code));
        await repository.DidNotReceive().SightingExistsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await AssertNoWriteAsync();
    }

    [Fact]
    public async Task Create_rejects_text_over_the_limit_after_trimming()
    {
        SightingCommentResult tooLong = await CreateService().CreateAsync(sightingId, authorId, new string('x', SightingCommentRules.TextMaxLength + 1), cancellationToken);
        SightingCommentResult padded = await CreateService().CreateAsync(sightingId, authorId, $" {new string('x', SightingCommentRules.TextMaxLength)} ", cancellationToken);

        Assert.Equal([SightingCommentErrorCodes.CommentTextTooLong], tooLong.Errors.Select(error => error.Code));
        Assert.True(padded.Succeeded);
    }

    [Fact]
    public async Task Create_checks_the_sighting_before_the_cooldown()
    {
        LatestSubmission(now.UtcDateTime);

        SightingCommentResult result = await CreateService().CreateAsync(Guid.NewGuid(), authorId, "Hello", cancellationToken);

        Assert.Equal([SightingCommentErrorCodes.SightingNotFound], result.Errors.Select(error => error.Code));
        await repository.DidNotReceive().GetLatestSubmissionDateByAuthorAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await AssertNoWriteAsync();
    }

    [Fact]
    public async Task Create_is_blocked_by_the_comment_cooldown_with_the_remaining_seconds()
    {
        LatestSubmission(now.UtcDateTime.AddSeconds(-30));

        SightingCommentResult result = await CreateService().CreateAsync(sightingId, authorId, "Hello", cancellationToken);

        Assert.False(result.Succeeded);
        SightingError error = Assert.Single(result.Errors);
        Assert.Equal(SightingCommentErrorCodes.CooldownActive, error.Code);
        Assert.Equal("Wait 30 seconds before posting or editing another comment.", error.Description);
        Assert.Equal(30, result.RetryAfterSeconds);
        await repository.Received(1).GetLatestSubmissionDateByAuthorAsync(authorId, Arg.Any<CancellationToken>());
        await AssertNoWriteAsync();
    }

    [Fact]
    public async Task Create_is_allowed_exactly_60_seconds_after_the_latest_submission()
    {
        LatestSubmission(now.UtcDateTime.AddSeconds(-SightingRules.CooldownSeconds));

        SightingCommentResult result = await CreateService().CreateAsync(sightingId, authorId, "Hello", cancellationToken);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Create_returns_author_not_found_when_the_user_row_is_gone()
    {
        repository.CreateAsync(Arg.Any<NewSightingComment>(), Arg.Any<CancellationToken>()).Returns((SightingComment?)null);

        SightingCommentResult result = await CreateService().CreateAsync(sightingId, authorId, "Hello", cancellationToken);

        Assert.Equal([SightingCommentErrorCodes.AuthorNotFound], result.Errors.Select(error => error.Code));
    }

    [Fact]
    public async Task Update_sets_the_trimmed_text_and_the_edit_time()
    {
        SightingComment existing = StoreComment(authorId);
        repository
            .UpdateTextAsync(existing.Id, Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(existing with { Text = "Edited", EditedAt = now.UtcDateTime });

        SightingCommentResult result = await CreateService().UpdateAsync(sightingId, existing.Id, authorId, " Edited ", cancellationToken);

        Assert.True(result.Succeeded);
        await repository.Received(1).UpdateTextAsync(existing.Id, "Edited", now.UtcDateTime, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_validates_the_text_before_looking_up_the_comment()
    {
        SightingComment existing = StoreComment(Guid.NewGuid());

        SightingCommentResult result = await CreateService().UpdateAsync(sightingId, existing.Id, authorId, " ", cancellationToken);

        Assert.Equal([SightingCommentErrorCodes.CommentTextRequired], result.Errors.Select(error => error.Code));
        await repository.DidNotReceive().GetAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await AssertNoWriteAsync();
    }

    [Fact]
    public async Task Update_returns_comment_not_found_for_a_missing_comment()
    {
        SightingCommentResult result = await CreateService().UpdateAsync(sightingId, Guid.NewGuid(), authorId, "Edited", cancellationToken);

        Assert.Equal([SightingCommentErrorCodes.CommentNotFound], result.Errors.Select(error => error.Code));
        await AssertNoWriteAsync();
    }

    [Fact]
    public async Task Update_returns_comment_not_found_for_a_deleted_comment_even_for_another_user()
    {
        SightingComment existing = StoreComment(Guid.NewGuid(), deletedAt: now.UtcDateTime.AddMinutes(-5));

        SightingCommentResult result = await CreateService().UpdateAsync(sightingId, existing.Id, authorId, "Edited", cancellationToken);

        Assert.Equal([SightingCommentErrorCodes.CommentNotFound], result.Errors.Select(error => error.Code));
        await AssertNoWriteAsync();
    }

    [Fact]
    public async Task Update_checks_authorship_before_the_cooldown()
    {
        SightingComment existing = StoreComment(Guid.NewGuid());
        LatestSubmission(now.UtcDateTime);

        SightingCommentResult result = await CreateService().UpdateAsync(sightingId, existing.Id, authorId, "Edited", cancellationToken);

        Assert.Equal([SightingCommentErrorCodes.NotCommentAuthor], result.Errors.Select(error => error.Code));
        await repository.DidNotReceive().GetLatestSubmissionDateByAuthorAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await AssertNoWriteAsync();
    }

    [Fact]
    public async Task Update_is_blocked_by_the_cooldown()
    {
        SightingComment existing = StoreComment(authorId);
        LatestSubmission(now.UtcDateTime.AddSeconds(-10));

        SightingCommentResult result = await CreateService().UpdateAsync(sightingId, existing.Id, authorId, "Edited", cancellationToken);

        Assert.Equal([SightingCommentErrorCodes.CooldownActive], result.Errors.Select(error => error.Code));
        Assert.Equal(50, result.RetryAfterSeconds);
        await AssertNoWriteAsync();
    }

    [Fact]
    public async Task Delete_sets_the_deletion_time_without_a_cooldown_check()
    {
        SightingComment existing = StoreComment(authorId);
        LatestSubmission(now.UtcDateTime);
        repository
            .SoftDeleteAsync(existing.Id, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(existing with { DeletedAt = now.UtcDateTime });

        SightingCommentResult result = await CreateService().DeleteAsync(sightingId, existing.Id, authorId, cancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(now.UtcDateTime, result.Comment!.DeletedAt);
        await repository.Received(1).SoftDeleteAsync(existing.Id, now.UtcDateTime, Arg.Any<CancellationToken>());
        await repository.DidNotReceive().GetLatestSubmissionDateByAuthorAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await repository.DidNotReceive().UpdateTextAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_returns_comment_not_found_for_a_missing_or_already_deleted_comment()
    {
        SightingComment deleted = StoreComment(authorId, deletedAt: now.UtcDateTime.AddMinutes(-1));

        SightingCommentResult missing = await CreateService().DeleteAsync(sightingId, Guid.NewGuid(), authorId, cancellationToken);
        SightingCommentResult again = await CreateService().DeleteAsync(sightingId, deleted.Id, authorId, cancellationToken);

        Assert.Equal([SightingCommentErrorCodes.CommentNotFound], missing.Errors.Select(error => error.Code));
        Assert.Equal([SightingCommentErrorCodes.CommentNotFound], again.Errors.Select(error => error.Code));
        await AssertNoWriteAsync();
    }

    [Fact]
    public async Task Delete_rejects_another_users_comment()
    {
        SightingComment existing = StoreComment(Guid.NewGuid());

        SightingCommentResult result = await CreateService().DeleteAsync(sightingId, existing.Id, authorId, cancellationToken);

        Assert.Equal([SightingCommentErrorCodes.NotCommentAuthor], result.Errors.Select(error => error.Code));
        await AssertNoWriteAsync();
    }

    private SightingCommentService CreateService()
    {
        return new(repository, new SubmissionCooldownProvider(timeProvider), timeProvider);
    }

    private void LatestSubmission(DateTime value)
    {
        repository.GetLatestSubmissionDateByAuthorAsync(authorId, Arg.Any<CancellationToken>()).Returns(value);
    }

    private SightingComment StoreComment(Guid commentAuthorId, DateTime? deletedAt = null)
    {
        SightingComment comment = Comment(commentAuthorId, "Original") with { DeletedAt = deletedAt };
        repository.GetAsync(sightingId, comment.Id, Arg.Any<CancellationToken>()).Returns(comment);

        return comment;
    }

    private SightingComment Comment(Guid commentAuthorId, string text)
    {
        return new(Guid.NewGuid(), sightingId, commentAuthorId, "Pidgey", text, now.UtcDateTime.AddMinutes(-10), null, null);
    }

    private async Task AssertNoWriteAsync()
    {
        await repository.DidNotReceive().CreateAsync(Arg.Any<NewSightingComment>(), Arg.Any<CancellationToken>());
        await repository.DidNotReceive().UpdateTextAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await repository.DidNotReceive().SoftDeleteAsync(Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }
}

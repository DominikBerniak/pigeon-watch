using PigeonWatch.BusinessObjects;
using PigeonWatch.Data.Repositories;

namespace PigeonWatch.BusinessLogic.Services;

public class SightingCommentService(
    ISightingCommentRepository sightingCommentRepository,
    ISubmissionCooldownProvider submissionCooldownProvider,
    TimeProvider timeProvider) : ISightingCommentService
{
    public async Task<SightingCommentPageResult> ListAsync(Guid sightingId, int page, CancellationToken cancellationToken = default)
    {
        if (page < 1)
            return SightingCommentPageResult.Failure([new SightingError(SightingCommentErrorCodes.InvalidPage, "Page must be 1 or greater.")]);

        if (page > int.MaxValue / SightingCommentRules.PageSize)
            return SightingCommentPageResult.Failure([new SightingError(SightingCommentErrorCodes.InvalidPage, "Page is too large.")]);

        if (!await sightingCommentRepository.SightingExistsAsync(sightingId, cancellationToken))
            return SightingCommentPageResult.Failure([SightingNotFound()]);

        SightingCommentPage comments = await sightingCommentRepository.ListPageAsync(sightingId, page, SightingCommentRules.PageSize, cancellationToken);

        return SightingCommentPageResult.Success(comments);
    }

    public async Task<SightingCommentResult> CreateAsync(Guid sightingId, Guid authorId, string text, CancellationToken cancellationToken = default)
    {
        string trimmed = text.Trim();
        SightingError? invalidText = ValidateText(trimmed);

        if (invalidText is not null)
            return SightingCommentResult.Failure([invalidText]);

        if (!await sightingCommentRepository.SightingExistsAsync(sightingId, cancellationToken))
            return SightingCommentResult.Failure([SightingNotFound()]);

        SightingCommentResult? cooldown = await CheckCooldownAsync(authorId, cancellationToken);

        if (cooldown is not null)
            return cooldown;

        SightingComment? created = await sightingCommentRepository.CreateAsync(new NewSightingComment(sightingId, authorId, trimmed), cancellationToken);

        if (created is null)
            return SightingCommentResult.Failure([new SightingError(SightingCommentErrorCodes.AuthorNotFound, "The commenting user does not exist.")]);

        return SightingCommentResult.Success(created);
    }

    public async Task<SightingCommentResult> UpdateAsync(Guid sightingId, Guid commentId, Guid userId, string text, CancellationToken cancellationToken = default)
    {
        string trimmed = text.Trim();
        SightingError? invalidText = ValidateText(trimmed);

        if (invalidText is not null)
            return SightingCommentResult.Failure([invalidText]);

        SightingComment? existing = await sightingCommentRepository.GetAsync(sightingId, commentId, cancellationToken);

        if (existing is null || existing.DeletedAt is not null)
            return SightingCommentResult.Failure([CommentNotFound()]);

        if (existing.AuthorId != userId)
            return SightingCommentResult.Failure([NotCommentAuthor()]);

        SightingCommentResult? cooldown = await CheckCooldownAsync(userId, cancellationToken);

        if (cooldown is not null)
            return cooldown;

        SightingComment? updated = await sightingCommentRepository.UpdateTextAsync(commentId, trimmed, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);

        if (updated is null)
            return SightingCommentResult.Failure([CommentNotFound()]);

        return SightingCommentResult.Success(updated);
    }

    public async Task<SightingCommentResult> DeleteAsync(Guid sightingId, Guid commentId, Guid userId, CancellationToken cancellationToken = default)
    {
        SightingComment? existing = await sightingCommentRepository.GetAsync(sightingId, commentId, cancellationToken);

        if (existing is null || existing.DeletedAt is not null)
            return SightingCommentResult.Failure([CommentNotFound()]);

        if (existing.AuthorId != userId)
            return SightingCommentResult.Failure([NotCommentAuthor()]);

        SightingComment? deleted = await sightingCommentRepository.SoftDeleteAsync(commentId, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);

        if (deleted is null)
            return SightingCommentResult.Failure([CommentNotFound()]);

        return SightingCommentResult.Success(deleted);
    }

    private async Task<SightingCommentResult?> CheckCooldownAsync(Guid authorId, CancellationToken cancellationToken = default)
    {
        DateTime? latestSubmission = await sightingCommentRepository.GetLatestSubmissionDateByAuthorAsync(authorId, cancellationToken);
        int? retryAfterSeconds = submissionCooldownProvider.GetRetryAfterSeconds(latestSubmission);

        if (retryAfterSeconds is null)
            return null;

        return SightingCommentResult.CooldownFailure(
            new SightingError(SightingCommentErrorCodes.CooldownActive, $"Wait {retryAfterSeconds} seconds before posting or editing another comment."),
            retryAfterSeconds.Value);
    }

    private static SightingError? ValidateText(string text)
    {
        if (text.Length == 0)
            return new SightingError(SightingCommentErrorCodes.CommentTextRequired, "A comment needs some text.");

        if (text.Length > SightingCommentRules.TextMaxLength)
            return new SightingError(SightingCommentErrorCodes.CommentTextTooLong, $"A comment must be at most {SightingCommentRules.TextMaxLength} characters.");

        return null;
    }

    private static SightingError SightingNotFound()
    {
        return new SightingError(SightingCommentErrorCodes.SightingNotFound, "The sighting does not exist.");
    }

    private static SightingError CommentNotFound()
    {
        return new SightingError(SightingCommentErrorCodes.CommentNotFound, "The comment does not exist.");
    }

    private static SightingError NotCommentAuthor()
    {
        return new SightingError(SightingCommentErrorCodes.NotCommentAuthor, "Only the author can change this comment.");
    }
}

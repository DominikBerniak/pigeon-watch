using PigeonWatch.BusinessObjects;
using PigeonWatch.Data.Repositories;

namespace PigeonWatch.BusinessLogic.Services;

public class SightingThreadService(ISightingThreadRepository sightingThreadRepository, TimeProvider timeProvider) : ISightingThreadService
{
    public async Task<SightingThreadResult> StartAsync(Guid sightingId, Guid userId, string text, CancellationToken cancellationToken = default)
    {
        string trimmed = text.Trim();
        SightingError? invalidText = ValidateText(trimmed);

        if (invalidText is not null)
            return SightingThreadResult.Failure([invalidText]);

        Guid? ownerId = await sightingThreadRepository.GetSightingOwnerAsync(sightingId, cancellationToken);

        if (ownerId is null)
            return SightingThreadResult.Failure([SightingNotFound()]);

        if (ownerId.Value == userId)
            return SightingThreadResult.Failure([new SightingError(SightingMessageErrorCodes.OwnSightingContact, "The owner cannot start a thread on their own sighting.")]);

        (SightingThread Thread, SightingMessage Message)? started = await sightingThreadRepository.StartOrAppendAsync(sightingId, userId, trimmed, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);

        if (started is null)
        {
            if (await sightingThreadRepository.GetSightingOwnerAsync(sightingId, cancellationToken) is null)
                return SightingThreadResult.Failure([SightingNotFound()]);

            return SightingThreadResult.Failure([SenderNotFound()]);
        }

        return SightingThreadResult.Success(started.Value.Thread, started.Value.Message);
    }

    public async Task<SightingThreadResult> ReplyAsync(Guid sightingId, Guid threadId, Guid userId, string text, CancellationToken cancellationToken = default)
    {
        string trimmed = text.Trim();
        SightingError? invalidText = ValidateText(trimmed);

        if (invalidText is not null)
            return SightingThreadResult.Failure([invalidText]);

        if (await sightingThreadRepository.GetSightingOwnerAsync(sightingId, cancellationToken) is null)
            return SightingThreadResult.Failure([SightingNotFound()]);

        SightingThread? thread = await sightingThreadRepository.GetThreadForUserAsync(sightingId, threadId, userId, cancellationToken);

        if (thread is null)
            return SightingThreadResult.Failure([ThreadNotFound()]);

        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        SightingMessage? message = await sightingThreadRepository.AppendAsync(sightingId, threadId, userId, trimmed, now, cancellationToken);

        if (message is null)
            return SightingThreadResult.Failure([SenderNotFound()]);

        return SightingThreadResult.Success(thread with { LastMessageAt = now, HasUnread = false }, message);
    }

    public async Task<SightingThreadPageResult> ListForSightingAsync(Guid sightingId, Guid userId, int page, CancellationToken cancellationToken = default)
    {
        SightingError? invalidPage = ValidatePage(page);

        if (invalidPage is not null)
            return SightingThreadPageResult.Failure([invalidPage]);

        if (await sightingThreadRepository.GetSightingOwnerAsync(sightingId, cancellationToken) is null)
            return SightingThreadPageResult.Failure([SightingNotFound()]);

        SightingThreadPage threads = await sightingThreadRepository.ListThreadsForSightingAsync(sightingId, userId, page, SightingMessageRules.PageSize, cancellationToken);

        return SightingThreadPageResult.Success(threads);
    }

    public async Task<SightingThreadPageResult> ListForUserAsync(Guid userId, int page, CancellationToken cancellationToken = default)
    {
        SightingError? invalidPage = ValidatePage(page);

        if (invalidPage is not null)
            return SightingThreadPageResult.Failure([invalidPage]);

        SightingThreadPage threads = await sightingThreadRepository.ListThreadsForUserAsync(userId, page, SightingMessageRules.PageSize, cancellationToken);

        return SightingThreadPageResult.Success(threads);
    }

    public async Task<SightingMessagePageResult> ListMessagesAsync(Guid sightingId, Guid threadId, Guid userId, int page, CancellationToken cancellationToken = default)
    {
        SightingError? invalidPage = ValidatePage(page);

        if (invalidPage is not null)
            return SightingMessagePageResult.Failure([invalidPage]);

        if (await sightingThreadRepository.GetSightingOwnerAsync(sightingId, cancellationToken) is null)
            return SightingMessagePageResult.Failure([SightingNotFound()]);

        if (!await sightingThreadRepository.IsThreadVisibleToUserAsync(sightingId, threadId, userId, cancellationToken))
            return SightingMessagePageResult.Failure([ThreadNotFound()]);

        SightingMessagePage messages = await sightingThreadRepository.ListMessagesAsync(sightingId, threadId, userId, page, SightingMessageRules.PageSize, cancellationToken);

        return SightingMessagePageResult.Success(messages);
    }

    public async Task<SightingThreadReadResult> MarkReadAsync(Guid sightingId, Guid threadId, Guid userId, DateTime seenAt, CancellationToken cancellationToken = default)
    {
        if (await sightingThreadRepository.GetSightingOwnerAsync(sightingId, cancellationToken) is null)
            return SightingThreadReadResult.Failure([SightingNotFound()]);

        bool marked = await sightingThreadRepository.MarkReadAsync(sightingId, threadId, userId, seenAt, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);

        if (!marked)
            return SightingThreadReadResult.Failure([ThreadNotFound()]);

        return SightingThreadReadResult.Success();
    }

    public async Task<int> CountUnreadMessagesAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        int count = await sightingThreadRepository.CountUnreadMessagesAsync(userId, cancellationToken);

        return count;
    }

    private static SightingError? ValidateText(string text)
    {
        if (text.Length == 0)
            return new SightingError(SightingMessageErrorCodes.MessageTextRequired, "A message needs some text.");

        if (text.Length > SightingMessageRules.TextMaxLength)
            return new SightingError(SightingMessageErrorCodes.MessageTextTooLong, $"A message must be at most {SightingMessageRules.TextMaxLength} characters.");

        return null;
    }

    private static SightingError? ValidatePage(int page)
    {
        if (page < 1)
            return new SightingError(SightingMessageErrorCodes.InvalidPage, "Page must be 1 or greater.");

        if (page > int.MaxValue / SightingMessageRules.PageSize)
            return new SightingError(SightingMessageErrorCodes.InvalidPage, "Page is too large.");

        return null;
    }

    private static SightingError SightingNotFound()
    {
        return new SightingError(SightingMessageErrorCodes.SightingNotFound, "The sighting does not exist.");
    }

    private static SightingError ThreadNotFound()
    {
        return new SightingError(SightingMessageErrorCodes.ThreadNotFound, "The thread does not exist.");
    }

    private static SightingError SenderNotFound()
    {
        return new SightingError(SightingMessageErrorCodes.SenderNotFound, "The sending user does not exist.");
    }
}

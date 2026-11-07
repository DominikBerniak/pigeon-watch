namespace PigeonWatch.BusinessObjects;

public sealed record SightingCommentResult(bool Succeeded, IReadOnlyList<SightingError> Errors, SightingComment? Comment, int? RetryAfterSeconds)
{
    public static SightingCommentResult Success(SightingComment comment)
    {
        return new(true, [], comment, null);
    }

    public static SightingCommentResult Failure(IReadOnlyList<SightingError> errors)
    {
        return new(false, errors, null, null);
    }

    public static SightingCommentResult CooldownFailure(SightingError error, int retryAfterSeconds)
    {
        return new(false, [error], null, retryAfterSeconds);
    }
}

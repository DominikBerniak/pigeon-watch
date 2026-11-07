namespace PigeonWatch.BusinessObjects;

public sealed record SightingCommentPageResult(bool Succeeded, IReadOnlyList<SightingError> Errors, SightingCommentPage Page)
{
    public static SightingCommentPageResult Success(SightingCommentPage page)
    {
        return new(true, [], page);
    }

    public static SightingCommentPageResult Failure(IReadOnlyList<SightingError> errors)
    {
        return new(false, errors, new SightingCommentPage([], 0));
    }
}

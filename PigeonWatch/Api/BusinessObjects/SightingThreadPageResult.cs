namespace PigeonWatch.BusinessObjects;

public sealed record SightingThreadPageResult(bool Succeeded, IReadOnlyList<SightingError> Errors, SightingThreadPage Page)
{
    public static SightingThreadPageResult Success(SightingThreadPage page)
    {
        return new(true, [], page);
    }

    public static SightingThreadPageResult Failure(IReadOnlyList<SightingError> errors)
    {
        return new(false, errors, new SightingThreadPage([], 0));
    }
}

namespace PigeonWatch.BusinessObjects;

public sealed record SightingListResult(bool Succeeded, IReadOnlyList<SightingError> Errors, SightingPage Page)
{
    public static SightingListResult Success(SightingPage page)
    {
        return new(true, [], page);
    }

    public static SightingListResult Failure(IReadOnlyList<SightingError> errors)
    {
        return new(false, errors, new SightingPage([], 0));
    }
}

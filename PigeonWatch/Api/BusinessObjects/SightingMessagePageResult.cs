namespace PigeonWatch.BusinessObjects;

public sealed record SightingMessagePageResult(bool Succeeded, IReadOnlyList<SightingError> Errors, SightingMessagePage Page)
{
    public static SightingMessagePageResult Success(SightingMessagePage page)
    {
        return new(true, [], page);
    }

    public static SightingMessagePageResult Failure(IReadOnlyList<SightingError> errors)
    {
        return new(false, errors, new SightingMessagePage([], 0));
    }
}

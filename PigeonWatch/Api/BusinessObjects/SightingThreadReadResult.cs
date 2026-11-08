namespace PigeonWatch.BusinessObjects;

public sealed record SightingThreadReadResult(bool Succeeded, IReadOnlyList<SightingError> Errors)
{
    public static SightingThreadReadResult Success()
    {
        return new(true, []);
    }

    public static SightingThreadReadResult Failure(IReadOnlyList<SightingError> errors)
    {
        return new(false, errors);
    }
}

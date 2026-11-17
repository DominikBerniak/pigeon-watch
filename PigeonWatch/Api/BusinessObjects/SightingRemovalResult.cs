namespace PigeonWatch.BusinessObjects;

public sealed record SightingRemovalResult(bool Succeeded, IReadOnlyList<SightingError> Errors, Sighting? Sighting)
{
    public static SightingRemovalResult Success(Sighting sighting)
    {
        return new(true, [], sighting);
    }

    public static SightingRemovalResult Failure(IReadOnlyList<SightingError> errors)
    {
        return new(false, errors, null);
    }
}

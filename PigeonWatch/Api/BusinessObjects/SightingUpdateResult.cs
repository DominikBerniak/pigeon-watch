namespace PigeonWatch.BusinessObjects;

public sealed record SightingUpdateResult(bool Succeeded, IReadOnlyList<SightingError> Errors, Sighting? Sighting, SightingEditPermissions? Permissions)
{
    public static SightingUpdateResult Success(Sighting sighting, SightingEditPermissions? permissions)
    {
        return new(true, [], sighting, permissions);
    }

    public static SightingUpdateResult Failure(IReadOnlyList<SightingError> errors)
    {
        return new(false, errors, null, null);
    }
}

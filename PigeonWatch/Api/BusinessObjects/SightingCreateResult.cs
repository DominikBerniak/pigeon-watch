namespace PigeonWatch.BusinessObjects;

public sealed record SightingCreateResult(
    bool Succeeded,
    IReadOnlyList<SightingError> Errors,
    Sighting? Sighting,
    int? RetryAfterSeconds,
    SightingEditPermissions? Permissions)
{
    public static SightingCreateResult Success(Sighting sighting, SightingEditPermissions? permissions)
    {
        return new(true, [], sighting, null, permissions);
    }

    public static SightingCreateResult Failure(IReadOnlyList<SightingError> errors)
    {
        return new(false, errors, null, null, null);
    }

    public static SightingCreateResult CooldownFailure(SightingError error, int retryAfterSeconds)
    {
        return new(false, [error], null, retryAfterSeconds, null);
    }
}

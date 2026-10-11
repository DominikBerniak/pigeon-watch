namespace PigeonWatch.BusinessObjects;

public sealed record SightingCreateResult(bool Succeeded, IReadOnlyList<SightingError> Errors, Sighting? Sighting, int? RetryAfterSeconds)
{
    public static SightingCreateResult Success(Sighting sighting)
    {
        return new(true, [], sighting, null);
    }

    public static SightingCreateResult Failure(IReadOnlyList<SightingError> errors)
    {
        return new(false, errors, null, null);
    }

    public static SightingCreateResult CooldownFailure(SightingError error, int retryAfterSeconds)
    {
        return new(false, [error], null, retryAfterSeconds);
    }
}

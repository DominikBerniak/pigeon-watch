namespace PigeonWatch.BusinessObjects;

public sealed record SightingNearbyResult(bool Succeeded, IReadOnlyList<SightingError> Errors, IReadOnlyList<Sighting> Sightings)
{
    public static SightingNearbyResult Success(IReadOnlyList<Sighting> sightings)
    {
        return new(true, [], sightings);
    }

    public static SightingNearbyResult Failure(IReadOnlyList<SightingError> errors)
    {
        return new(false, errors, []);
    }
}

using PigeonWatch.BusinessObjects;
using PigeonWatch.Data.Repositories;

namespace PigeonWatch.BusinessLogic.Services;

public class SightingService(
    ISightingRepository sightingRepository,
    ISightingStatusTransitionProvider sightingStatusTransitionProvider,
    IGeoDistanceCalculator geoDistanceCalculator,
    TimeProvider timeProvider) : ISightingService
{
    public async Task<SightingCreateResult> CreateAsync(NewSighting sighting, CancellationToken cancellationToken = default)
    {
        NewSighting trimmed = sighting with
        {
            Name = string.IsNullOrWhiteSpace(sighting.Name) ? null : sighting.Name.Trim(),
            Description = sighting.Description.Trim(),
            Behaviour = sighting.Behaviour.Trim()
        };

        List<SightingError> errors = Validate(trimmed);

        if (errors.Count > 0)
            return SightingCreateResult.Failure(errors);

        DateTime? latestCreateDate = await sightingRepository.GetLatestCreateDateByReporterAsync(trimmed.ReporterId, cancellationToken);

        if (latestCreateDate is not null)
        {
            TimeSpan cooldown = TimeSpan.FromSeconds(SightingRules.CooldownSeconds);
            TimeSpan elapsed = timeProvider.GetUtcNow().UtcDateTime - latestCreateDate.Value;

            if (elapsed < cooldown)
            {
                int retryAfterSeconds = (int)Math.Min(Math.Ceiling((cooldown - elapsed).TotalSeconds), SightingRules.CooldownSeconds);

                return SightingCreateResult.CooldownFailure(
                    new SightingError(SightingErrorCodes.CooldownActive, $"Wait {retryAfterSeconds} seconds before reporting another sighting."),
                    retryAfterSeconds);
            }
        }

        Sighting? created = await sightingRepository.CreateAsync(trimmed, cancellationToken);

        if (created is null)
            return SightingCreateResult.Failure([new SightingError(SightingErrorCodes.ReporterNotFound, "The reporting user does not exist.")]);

        return SightingCreateResult.Success(created);
    }

    public async Task<Sighting?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await sightingRepository.GetByIdAsync(id, cancellationToken);
    }

    public async Task<SightingNearbyResult> GetNearbyAsync(
        double latitude,
        double longitude,
        IReadOnlyCollection<SightingStatus> statuses,
        IReadOnlyCollection<SightingCriticality> criticalities,
        CancellationToken cancellationToken = default)
    {
        List<SightingStatus> openStatuses = Enum.GetValues<SightingStatus>()
            .Where(status => !sightingStatusTransitionProvider.IsResolved(status))
            .ToList();
        List<SightingError> errors = ValidateNearbyFilters(statuses, criticalities, openStatuses);

        if (errors.Count > 0)
            return SightingNearbyResult.Failure(errors);

        GeoBoundingBox box = geoDistanceCalculator.GetBoundingBox(latitude, longitude, SightingRules.NearbyRadiusKilometers);
        List<SightingStatus> requestedStatuses = statuses.Count == 0 ? openStatuses : statuses.Distinct().ToList();
        List<SightingCriticality> requestedCriticalities = criticalities.Distinct().ToList();

        IReadOnlyList<Sighting> candidates = await sightingRepository.ListInBoxAsync(
            box.MinLatitude,
            box.MaxLatitude,
            box.MinLongitude,
            box.MaxLongitude,
            requestedStatuses,
            requestedCriticalities,
            SightingRules.NearbyBoxMaxRows,
            cancellationToken);

        List<Sighting> nearby = candidates
            .Where(candidate => geoDistanceCalculator.GetDistanceKilometers(latitude, longitude, candidate.Latitude, candidate.Longitude) <= SightingRules.NearbyRadiusKilometers)
            .OrderByDescending(candidate => candidate.CreatedAt)
            .Take(SightingRules.NearbyMaxResults)
            .ToList();

        return SightingNearbyResult.Success(nearby);
    }

    public async Task<IReadOnlyList<Sighting>> GetMineAsync(Guid reporterId, CancellationToken cancellationToken = default)
    {
        return await sightingRepository.ListByReporterAsync(reporterId, SightingRules.MineMaxResults, cancellationToken);
    }

    private List<SightingError> Validate(NewSighting sighting)
    {
        List<SightingError> errors = [];

        if (!sightingStatusTransitionProvider.IsValidInitialStatus(sighting.Status))
            errors.Add(new SightingError(SightingErrorCodes.InvalidInitialStatus, "A new sighting must be Spotted or Caught."));

        if (!Enum.IsDefined(sighting.Criticality))
            errors.Add(new SightingError(SightingErrorCodes.InvalidCriticality, "Criticality must be Monitor, Needs help soon or Urgent."));

        if (!IsValidLocation(sighting.Latitude, sighting.Longitude))
            errors.Add(new SightingError(SightingErrorCodes.InvalidLocation, "Latitude must be between -90 and 90 and longitude between -180 and 180."));

        return errors;
    }

    private static List<SightingError> ValidateNearbyFilters(
        IReadOnlyCollection<SightingStatus> statuses,
        IReadOnlyCollection<SightingCriticality> criticalities,
        IReadOnlyCollection<SightingStatus> openStatuses)
    {
        List<SightingError> errors = [];

        if (statuses.Any(status => !openStatuses.Contains(status)))
            errors.Add(new SightingError(SightingErrorCodes.InvalidFilterStatus, "Status filter accepts only Spotted, Caught, At the vet, In home care or Awaiting adoption."));

        if (criticalities.Any(criticality => !Enum.IsDefined(criticality)))
            errors.Add(new SightingError(SightingErrorCodes.InvalidFilterCriticality, "Criticality filter accepts only Monitor, Needs help soon or Urgent."));

        return errors;
    }

    private static bool IsValidLocation(double latitude, double longitude)
    {
        return double.IsFinite(latitude)
            && double.IsFinite(longitude)
            && latitude >= -90
            && latitude <= 90
            && longitude >= -180
            && longitude <= 180;
    }
}

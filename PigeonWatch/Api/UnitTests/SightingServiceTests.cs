using NSubstitute;
using PigeonWatch.BusinessLogic.Services;
using PigeonWatch.BusinessObjects;
using PigeonWatch.Data.Repositories;

namespace PigeonWatch.UnitTests;

public class SightingServiceTests
{
    private const double centreLatitude = 52.0;
    private const double centreLongitude = 21.0;
    private const double kilometersPerLatitudeDegree = 111.19508;

    private static readonly DateTimeOffset now = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    private readonly ISightingRepository sightingRepository = Substitute.For<ISightingRepository>();
    private readonly ManualTimeProvider timeProvider = new(now);
    private readonly GeoDistanceCalculator geoDistanceCalculator = new();
    private readonly CancellationToken cancellationToken = TestContext.Current.CancellationToken;

    public SightingServiceTests()
    {
        sightingRepository
            .CreateAsync(Arg.Any<NewSighting>(), Arg.Any<CancellationToken>())
            .Returns(call => ToSighting(call.Arg<NewSighting>()));
        sightingRepository
            .GetLatestCreateDateByReporterAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((DateTime?)null);
    }

    [Fact]
    public async Task Create_succeeds_when_the_reporter_has_no_prior_sighting()
    {
        NewSighting sighting = NewSighting();

        SightingCreateResult result = await CreateService().CreateAsync(sighting, cancellationToken);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Sighting);
        Assert.Null(result.RetryAfterSeconds);
        await sightingRepository.Received(1).CreateAsync(sighting, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_is_blocked_59_seconds_after_the_previous_sighting_with_1_second_remaining()
    {
        NewSighting sighting = NewSighting();
        LatestCreateDate(sighting.ReporterId, now.AddSeconds(-59));

        SightingCreateResult result = await CreateService().CreateAsync(sighting, cancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal([SightingErrorCodes.CooldownActive], result.Errors.Select(error => error.Code));
        Assert.Equal(1, result.RetryAfterSeconds);
        await sightingRepository.DidNotReceive().CreateAsync(Arg.Any<NewSighting>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_rounds_the_remaining_cooldown_up_to_whole_seconds()
    {
        NewSighting sighting = NewSighting();
        LatestCreateDate(sighting.ReporterId, now.AddSeconds(-10.2));

        SightingCreateResult result = await CreateService().CreateAsync(sighting, cancellationToken);

        Assert.Equal(50, result.RetryAfterSeconds);
    }

    [Fact]
    public async Task Create_is_allowed_exactly_60_seconds_after_the_previous_sighting()
    {
        NewSighting sighting = NewSighting();
        LatestCreateDate(sighting.ReporterId, now.AddSeconds(-60));

        SightingCreateResult result = await CreateService().CreateAsync(sighting, cancellationToken);

        Assert.True(result.Succeeded);
        await sightingRepository.Received(1).CreateAsync(sighting, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(3)]
    [InlineData(6)]
    [InlineData(9)]
    [InlineData(0)]
    [InlineData(42)]
    public async Task Create_rejects_a_status_other_than_spotted_or_caught(int status)
    {
        SightingCreateResult result = await CreateService().CreateAsync(
            NewSighting() with { Status = (SightingStatus)status },
            cancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal([SightingErrorCodes.InvalidInitialStatus], result.Errors.Select(error => error.Code));
        await sightingRepository.DidNotReceive().CreateAsync(Arg.Any<NewSighting>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Create_accepts_spotted_and_caught(int status)
    {
        SightingCreateResult result = await CreateService().CreateAsync(
            NewSighting() with { Status = (SightingStatus)status },
            cancellationToken);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public async Task Create_rejects_a_criticality_outside_1_to_3(int criticality)
    {
        SightingCreateResult result = await CreateService().CreateAsync(
            NewSighting() with { Criticality = (SightingCriticality)criticality },
            cancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal([SightingErrorCodes.InvalidCriticality], result.Errors.Select(error => error.Code));
        await sightingRepository.DidNotReceive().CreateAsync(Arg.Any<NewSighting>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(90.0001, 0)]
    [InlineData(-90.0001, 0)]
    [InlineData(0, 180.0001)]
    [InlineData(0, -180.0001)]
    [InlineData(double.NaN, 0)]
    public async Task Create_rejects_a_location_out_of_range(double latitude, double longitude)
    {
        SightingCreateResult result = await CreateService().CreateAsync(
            NewSighting() with { Latitude = latitude, Longitude = longitude },
            cancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal([SightingErrorCodes.InvalidLocation], result.Errors.Select(error => error.Code));
    }

    [Theory]
    [InlineData(90, 180)]
    [InlineData(-90, -180)]
    public async Task Create_accepts_a_location_on_the_range_edges(double latitude, double longitude)
    {
        SightingCreateResult result = await CreateService().CreateAsync(
            NewSighting() with { Latitude = latitude, Longitude = longitude },
            cancellationToken);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Create_validates_before_reading_the_cooldown()
    {
        SightingCreateResult result = await CreateService().CreateAsync(
            NewSighting() with { Status = SightingStatus.AtTheVet },
            cancellationToken);

        Assert.False(result.Succeeded);
        await sightingRepository.DidNotReceive().GetLatestCreateDateByReporterAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_trims_text_and_stores_a_blank_name_as_null()
    {
        await CreateService().CreateAsync(
            NewSighting() with { Name = "   ", Description = "  Limping  ", Behaviour = " Cannot fly " },
            cancellationToken);

        await sightingRepository.Received(1).CreateAsync(
            Arg.Is<NewSighting>(stored => stored.Name == null && stored.Description == "Limping" && stored.Behaviour == "Cannot fly"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_returns_reporter_not_found_when_the_repository_finds_no_reporter()
    {
        sightingRepository
            .CreateAsync(Arg.Any<NewSighting>(), Arg.Any<CancellationToken>())
            .Returns((Sighting?)null);

        SightingCreateResult result = await CreateService().CreateAsync(NewSighting(), cancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal([SightingErrorCodes.ReporterNotFound], result.Errors.Select(error => error.Code));
    }

    [Fact]
    public async Task Get_returns_null_for_an_unknown_id()
    {
        Guid unknownId = Guid.NewGuid();
        sightingRepository.GetByIdAsync(unknownId, Arg.Any<CancellationToken>()).Returns((Sighting?)null);

        Sighting? sighting = await CreateService().GetAsync(unknownId, cancellationToken);

        Assert.Null(sighting);
    }

    [Fact]
    public async Task Get_returns_the_stored_sighting()
    {
        Sighting stored = SightingAt(centreLatitude, centreLongitude, now.UtcDateTime);
        sightingRepository.GetByIdAsync(stored.Id, Arg.Any<CancellationToken>()).Returns(stored);

        Sighting? sighting = await CreateService().GetAsync(stored.Id, cancellationToken);

        Assert.Equal(stored, sighting);
    }

    [Fact]
    public async Task Nearby_queries_the_bounding_box_for_open_statuses_only_capped_at_the_box_row_limit()
    {
        GeoBoundingBox box = geoDistanceCalculator.GetBoundingBox(centreLatitude, centreLongitude, SightingRules.NearbyRadiusKilometers);
        SightingStatus[] openStatuses =
            [SightingStatus.Spotted, SightingStatus.Caught, SightingStatus.AtTheVet, SightingStatus.InHomeCare, SightingStatus.AwaitingAdoption];
        CandidatesInBox([]);

        await CreateService().GetNearbyAsync(centreLatitude, centreLongitude, cancellationToken);

        await sightingRepository.Received(1).ListInBoxAsync(
            box.MinLatitude,
            box.MaxLatitude,
            box.MinLongitude,
            box.MaxLongitude,
            Arg.Is<IReadOnlyCollection<SightingStatus>>(statuses => statuses.Order().SequenceEqual(openStatuses)),
            SightingRules.NearbyBoxMaxRows,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Nearby_includes_a_point_just_inside_the_radius_and_excludes_one_just_outside()
    {
        Sighting inside = SightingAt(centreLatitude + (9.99 / kilometersPerLatitudeDegree), centreLongitude, now.UtcDateTime);
        Sighting outside = SightingAt(centreLatitude + (10.01 / kilometersPerLatitudeDegree), centreLongitude, now.UtcDateTime);
        CandidatesInBox([inside, outside]);

        IReadOnlyList<Sighting> nearby = await CreateService().GetNearbyAsync(centreLatitude, centreLongitude, cancellationToken);

        Assert.Equal([inside.Id], nearby.Select(sighting => sighting.Id));
    }

    [Fact]
    public async Task Nearby_excludes_a_box_corner_that_lies_outside_the_circle()
    {
        GeoBoundingBox box = geoDistanceCalculator.GetBoundingBox(centreLatitude, centreLongitude, SightingRules.NearbyRadiusKilometers);
        double cornerLatitude = centreLatitude + ((box.MaxLatitude - centreLatitude) * 0.9);
        double cornerLongitude = centreLongitude + ((box.MaxLongitude - centreLongitude) * 0.9);
        Sighting corner = SightingAt(cornerLatitude, cornerLongitude, now.UtcDateTime);
        CandidatesInBox([corner]);

        IReadOnlyList<Sighting> nearby = await CreateService().GetNearbyAsync(centreLatitude, centreLongitude, cancellationToken);

        Assert.InRange(cornerLatitude, box.MinLatitude, box.MaxLatitude);
        Assert.InRange(cornerLongitude, box.MinLongitude, box.MaxLongitude);
        Assert.Empty(nearby);
    }

    [Fact]
    public async Task Nearby_returns_at_most_200_sightings_newest_first()
    {
        List<Sighting> candidates = Enumerable.Range(0, SightingRules.NearbyMaxResults + 5)
            .Select(index => SightingAt(centreLatitude, centreLongitude, now.UtcDateTime.AddMinutes(-((index * 37) % 205))))
            .ToList();
        CandidatesInBox(candidates);

        IReadOnlyList<Sighting> nearby = await CreateService().GetNearbyAsync(centreLatitude, centreLongitude, cancellationToken);

        List<DateTime> expected = candidates
            .Select(sighting => sighting.CreatedAt)
            .OrderByDescending(createdAt => createdAt)
            .Take(SightingRules.NearbyMaxResults)
            .ToList();
        Assert.Equal(SightingRules.NearbyMaxResults, nearby.Count);
        Assert.Equal(expected, nearby.Select(sighting => sighting.CreatedAt));
    }

    [Fact]
    public async Task Mine_asks_the_repository_for_the_reporters_50_newest_sightings()
    {
        Guid reporterId = Guid.NewGuid();
        List<Sighting> stored = [SightingAt(centreLatitude, centreLongitude, now.UtcDateTime)];
        sightingRepository.ListByReporterAsync(reporterId, SightingRules.MineMaxResults, Arg.Any<CancellationToken>()).Returns(stored);

        IReadOnlyList<Sighting> mine = await CreateService().GetMineAsync(reporterId, cancellationToken);

        Assert.Equal(stored, mine);
        await sightingRepository.Received(1).ListByReporterAsync(reporterId, 50, Arg.Any<CancellationToken>());
    }

    private SightingService CreateService()
    {
        return new(sightingRepository, new SightingStatusTransitionProvider(), geoDistanceCalculator, timeProvider);
    }

    private void LatestCreateDate(Guid reporterId, DateTimeOffset createdAt)
    {
        sightingRepository
            .GetLatestCreateDateByReporterAsync(reporterId, Arg.Any<CancellationToken>())
            .Returns(createdAt.UtcDateTime);
    }

    private void CandidatesInBox(IReadOnlyList<Sighting> candidates)
    {
        sightingRepository
            .ListInBoxAsync(
                Arg.Any<double>(),
                Arg.Any<double>(),
                Arg.Any<double>(),
                Arg.Any<double>(),
                Arg.Any<IReadOnlyCollection<SightingStatus>>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(candidates);
    }

    private static NewSighting NewSighting()
    {
        return new(
            Guid.NewGuid(),
            "Grey",
            "Limping near the fountain",
            "Cannot fly",
            centreLatitude,
            centreLongitude,
            SightingStatus.Spotted,
            SightingCriticality.Urgent);
    }

    private static Sighting ToSighting(NewSighting sighting)
    {
        return new(
            Guid.NewGuid(),
            sighting.Name,
            sighting.Description,
            sighting.Behaviour,
            sighting.Latitude,
            sighting.Longitude,
            sighting.Status,
            sighting.Criticality,
            "Pidgey",
            now.UtcDateTime);
    }

    private static Sighting SightingAt(double latitude, double longitude, DateTime createdAt)
    {
        return new(
            Guid.NewGuid(),
            null,
            "Limping",
            "Cannot fly",
            latitude,
            longitude,
            SightingStatus.Spotted,
            SightingCriticality.Monitor,
            "Pidgey",
            createdAt);
    }
}

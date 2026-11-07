using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PigeonWatch.BusinessLogic.Services;
using PigeonWatch.BusinessObjects;
using PigeonWatch.Data.Repositories;
using PigeonWatch.Data.Storage;

namespace PigeonWatch.UnitTests;

public class SightingServiceTests
{
    private const double centreLatitude = 52.0;
    private const double centreLongitude = 21.0;
    private const double kilometersPerLatitudeDegree = 111.19508;

    private static readonly DateTimeOffset now = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    private readonly ISightingRepository sightingRepository = Substitute.For<ISightingRepository>();
    private readonly IPhotoNormalizationProvider photoNormalizationProvider = Substitute.For<IPhotoNormalizationProvider>();
    private readonly IPhotoThumbnailProvider photoThumbnailProvider = Substitute.For<IPhotoThumbnailProvider>();
    private readonly ISightingPhotoStore sightingPhotoStore = Substitute.For<ISightingPhotoStore>();
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

        await CreateService().GetNearbyAsync(centreLatitude, centreLongitude, [], [], cancellationToken);

        await sightingRepository.Received(1).ListInBoxAsync(
            box.MinLatitude,
            box.MaxLatitude,
            box.MinLongitude,
            box.MaxLongitude,
            Arg.Is<IReadOnlyCollection<SightingStatus>>(statuses => statuses.Order().SequenceEqual(openStatuses)),
            Arg.Is<IReadOnlyCollection<SightingCriticality>>(criticalities => criticalities.Count == 0),
            SightingRules.NearbyBoxMaxRows,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Nearby_includes_a_point_just_inside_the_radius_and_excludes_one_just_outside()
    {
        Sighting inside = SightingAt(centreLatitude + (9.99 / kilometersPerLatitudeDegree), centreLongitude, now.UtcDateTime);
        Sighting outside = SightingAt(centreLatitude + (10.01 / kilometersPerLatitudeDegree), centreLongitude, now.UtcDateTime);
        CandidatesInBox([inside, outside]);

        IReadOnlyList<Sighting> nearby = (await CreateService().GetNearbyAsync(centreLatitude, centreLongitude, [], [], cancellationToken)).Sightings;

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

        IReadOnlyList<Sighting> nearby = (await CreateService().GetNearbyAsync(centreLatitude, centreLongitude, [], [], cancellationToken)).Sightings;

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

        IReadOnlyList<Sighting> nearby = (await CreateService().GetNearbyAsync(centreLatitude, centreLongitude, [], [], cancellationToken)).Sightings;

        List<DateTime> expected = candidates
            .Select(sighting => sighting.CreatedAt)
            .OrderByDescending(createdAt => createdAt)
            .Take(SightingRules.NearbyMaxResults)
            .ToList();
        Assert.Equal(SightingRules.NearbyMaxResults, nearby.Count);
        Assert.Equal(expected, nearby.Select(sighting => sighting.CreatedAt));
    }

    [Fact]
    public async Task Nearby_with_empty_filters_succeeds_and_returns_the_sightings_in_the_circle()
    {
        Sighting inside = SightingAt(centreLatitude, centreLongitude, now.UtcDateTime);
        CandidatesInBox([inside]);

        SightingNearbyResult result = await CreateService().GetNearbyAsync(centreLatitude, centreLongitude, [], [], cancellationToken);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Errors);
        Assert.Equal([inside.Id], result.Sightings.Select(sighting => sighting.Id));
    }

    [Fact]
    public async Task Nearby_with_a_status_filter_queries_only_the_requested_statuses_and_no_criticality_predicate()
    {
        CandidatesInBox([]);

        SightingNearbyResult result = await CreateService().GetNearbyAsync(
            centreLatitude,
            centreLongitude,
            [SightingStatus.Caught, SightingStatus.Spotted],
            [],
            cancellationToken);

        Assert.True(result.Succeeded);
        await sightingRepository.Received(1).ListInBoxAsync(
            Arg.Any<double>(),
            Arg.Any<double>(),
            Arg.Any<double>(),
            Arg.Any<double>(),
            Arg.Is<IReadOnlyCollection<SightingStatus>>(statuses => statuses.Order().SequenceEqual(new[] { SightingStatus.Spotted, SightingStatus.Caught })),
            Arg.Is<IReadOnlyCollection<SightingCriticality>>(criticalities => criticalities.Count == 0),
            SightingRules.NearbyBoxMaxRows,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Nearby_with_a_criticality_filter_queries_every_open_status_and_only_the_requested_criticalities()
    {
        SightingStatus[] openStatuses =
            [SightingStatus.Spotted, SightingStatus.Caught, SightingStatus.AtTheVet, SightingStatus.InHomeCare, SightingStatus.AwaitingAdoption];
        CandidatesInBox([]);

        SightingNearbyResult result = await CreateService().GetNearbyAsync(
            centreLatitude,
            centreLongitude,
            [],
            [SightingCriticality.Urgent],
            cancellationToken);

        Assert.True(result.Succeeded);
        await sightingRepository.Received(1).ListInBoxAsync(
            Arg.Any<double>(),
            Arg.Any<double>(),
            Arg.Any<double>(),
            Arg.Any<double>(),
            Arg.Is<IReadOnlyCollection<SightingStatus>>(statuses => statuses.Order().SequenceEqual(openStatuses)),
            Arg.Is<IReadOnlyCollection<SightingCriticality>>(criticalities => criticalities.SequenceEqual(new[] { SightingCriticality.Urgent })),
            SightingRules.NearbyBoxMaxRows,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Nearby_with_both_filters_queries_the_requested_statuses_and_criticalities_without_duplicates()
    {
        CandidatesInBox([]);

        SightingNearbyResult result = await CreateService().GetNearbyAsync(
            centreLatitude,
            centreLongitude,
            [SightingStatus.AtTheVet, SightingStatus.AtTheVet],
            [SightingCriticality.Monitor, SightingCriticality.NeedsHelpSoon, SightingCriticality.Monitor],
            cancellationToken);

        Assert.True(result.Succeeded);
        await sightingRepository.Received(1).ListInBoxAsync(
            Arg.Any<double>(),
            Arg.Any<double>(),
            Arg.Any<double>(),
            Arg.Any<double>(),
            Arg.Is<IReadOnlyCollection<SightingStatus>>(statuses => statuses.SequenceEqual(new[] { SightingStatus.AtTheVet })),
            Arg.Is<IReadOnlyCollection<SightingCriticality>>(criticalities =>
                criticalities.Order().SequenceEqual(new[] { SightingCriticality.Monitor, SightingCriticality.NeedsHelpSoon })),
            SightingRules.NearbyBoxMaxRows,
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(42)]
    public async Task Nearby_rejects_a_status_filter_value_that_is_undefined_or_resolved(int status)
    {
        SightingNearbyResult result = await CreateService().GetNearbyAsync(
            centreLatitude,
            centreLongitude,
            [SightingStatus.Spotted, (SightingStatus)status],
            [],
            cancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal([SightingErrorCodes.InvalidFilterStatus], result.Errors.Select(error => error.Code));
        Assert.Empty(result.Sightings);
        await AssertNoBoxQueryAsync();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(9)]
    public async Task Nearby_rejects_a_criticality_filter_value_outside_1_to_3(int criticality)
    {
        SightingNearbyResult result = await CreateService().GetNearbyAsync(
            centreLatitude,
            centreLongitude,
            [],
            [SightingCriticality.Urgent, (SightingCriticality)criticality],
            cancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal([SightingErrorCodes.InvalidFilterCriticality], result.Errors.Select(error => error.Code));
        await AssertNoBoxQueryAsync();
    }

    [Fact]
    public async Task Nearby_reports_a_rejected_status_and_a_rejected_criticality_together()
    {
        SightingNearbyResult result = await CreateService().GetNearbyAsync(
            centreLatitude,
            centreLongitude,
            [SightingStatus.Adopted],
            [(SightingCriticality)9],
            cancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(
            [SightingErrorCodes.InvalidFilterCriticality, SightingErrorCodes.InvalidFilterStatus],
            result.Errors.Select(error => error.Code).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task List_with_empty_statuses_asks_the_repository_for_the_open_statuses_in_the_radius()
    {
        GeoRadiusFilter radius = geoDistanceCalculator.GetRadiusFilter(centreLatitude, centreLongitude, SightingRules.NearbyRadiusKilometers);
        SightingStatus[] openStatuses =
            [SightingStatus.Spotted, SightingStatus.Caught, SightingStatus.AtTheVet, SightingStatus.InHomeCare, SightingStatus.AwaitingAdoption];
        Sighting stored = SightingAt(centreLatitude, centreLongitude, now.UtcDateTime);
        PageInRadius(new SightingPage([stored], 1));

        SightingListResult result = await CreateService().GetListPageAsync(ListQuery(), cancellationToken);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Errors);
        Assert.Equal([stored.Id], result.Page.Items.Select(sighting => sighting.Id));
        Assert.Equal(1, result.Page.TotalCount);
        await sightingRepository.Received(1).ListPageInRadiusAsync(
            radius,
            Arg.Is<IReadOnlyCollection<SightingStatus>>(statuses => statuses.Order().SequenceEqual(openStatuses)),
            Arg.Is<IReadOnlyCollection<SightingCriticality>>(criticalities => criticalities.Count == 0),
            0,
            SightingRules.ListDefaultPageSize,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task List_accepts_a_resolved_status_and_passes_it_through()
    {
        PageInRadius(new SightingPage([], 0));

        SightingListResult result = await CreateService().GetListPageAsync(
            ListQuery() with { Statuses = [SightingStatus.Adopted] },
            cancellationToken);

        Assert.True(result.Succeeded);
        await sightingRepository.Received(1).ListPageInRadiusAsync(
            Arg.Any<GeoRadiusFilter>(),
            Arg.Is<IReadOnlyCollection<SightingStatus>>(statuses => statuses.SequenceEqual(new[] { SightingStatus.Adopted })),
            Arg.Is<IReadOnlyCollection<SightingCriticality>>(criticalities => criticalities.Count == 0),
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task List_passes_the_requested_statuses_and_criticalities_without_duplicates()
    {
        PageInRadius(new SightingPage([], 0));

        SightingListResult result = await CreateService().GetListPageAsync(
            ListQuery() with
            {
                Statuses = [SightingStatus.Released, SightingStatus.Spotted, SightingStatus.Released],
                Criticalities = [SightingCriticality.Urgent, SightingCriticality.Monitor, SightingCriticality.Urgent]
            },
            cancellationToken);

        Assert.True(result.Succeeded);
        await sightingRepository.Received(1).ListPageInRadiusAsync(
            Arg.Any<GeoRadiusFilter>(),
            Arg.Is<IReadOnlyCollection<SightingStatus>>(statuses =>
                statuses.Order().SequenceEqual(new[] { SightingStatus.Spotted, SightingStatus.Released })),
            Arg.Is<IReadOnlyCollection<SightingCriticality>>(criticalities =>
                criticalities.Order().SequenceEqual(new[] { SightingCriticality.Monitor, SightingCriticality.Urgent })),
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(1, 10, 0)]
    [InlineData(2, 20, 20)]
    [InlineData(3, 30, 60)]
    public async Task List_skips_the_earlier_pages_and_takes_one_page(int page, int pageSize, int expectedSkip)
    {
        PageInRadius(new SightingPage([], 0));

        SightingListResult result = await CreateService().GetListPageAsync(
            ListQuery() with { Page = page, PageSize = pageSize },
            cancellationToken);

        Assert.True(result.Succeeded);
        await sightingRepository.Received(1).ListPageInRadiusAsync(
            Arg.Any<GeoRadiusFilter>(),
            Arg.Any<IReadOnlyCollection<SightingStatus>>(),
            Arg.Any<IReadOnlyCollection<SightingCriticality>>(),
            expectedSkip,
            pageSize,
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(42)]
    public async Task List_rejects_an_undefined_status(int status)
    {
        SightingListResult result = await CreateService().GetListPageAsync(
            ListQuery() with { Statuses = [SightingStatus.Spotted, (SightingStatus)status] },
            cancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal([SightingErrorCodes.InvalidFilterStatus], result.Errors.Select(error => error.Code));
        Assert.Empty(result.Page.Items);
        await AssertNoPageQueryAsync();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public async Task List_rejects_an_undefined_criticality(int criticality)
    {
        SightingListResult result = await CreateService().GetListPageAsync(
            ListQuery() with { Criticalities = [SightingCriticality.Urgent, (SightingCriticality)criticality] },
            cancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal([SightingErrorCodes.InvalidFilterCriticality], result.Errors.Select(error => error.Code));
        await AssertNoPageQueryAsync();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task List_rejects_a_page_below_1(int page)
    {
        SightingListResult result = await CreateService().GetListPageAsync(
            ListQuery() with { Page = page },
            cancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal([SightingErrorCodes.InvalidPage], result.Errors.Select(error => error.Code));
        await AssertNoPageQueryAsync();
    }

    [Theory]
    [InlineData(int.MaxValue)]
    [InlineData(int.MaxValue / 10 + 2)]
    public async Task List_rejects_a_page_whose_offset_overflows(int page)
    {
        SightingListResult result = await CreateService().GetListPageAsync(
            ListQuery() with { Page = page, PageSize = 10 },
            cancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal([SightingErrorCodes.InvalidPage], result.Errors.Select(error => error.Code));
        await AssertNoPageQueryAsync();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(31)]
    [InlineData(-10)]
    public async Task List_rejects_a_page_size_other_than_10_20_or_30(int pageSize)
    {
        SightingListResult result = await CreateService().GetListPageAsync(
            ListQuery() with { PageSize = pageSize },
            cancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal([SightingErrorCodes.InvalidPageSize], result.Errors.Select(error => error.Code));
        await AssertNoPageQueryAsync();
    }

    [Fact]
    public async Task List_reports_every_rejected_value_together()
    {
        SightingListResult result = await CreateService().GetListPageAsync(
            ListQuery() with
            {
                Statuses = [(SightingStatus)42],
                Criticalities = [(SightingCriticality)9],
                Page = 0,
                PageSize = 15
            },
            cancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(
            [
                SightingErrorCodes.InvalidFilterCriticality,
                SightingErrorCodes.InvalidFilterStatus,
                SightingErrorCodes.InvalidPage,
                SightingErrorCodes.InvalidPageSize
            ],
            result.Errors.Select(error => error.Code).Order(StringComparer.Ordinal));
        await AssertNoPageQueryAsync();
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
        return new(
            sightingRepository,
            new SightingStatusTransitionProvider(),
            geoDistanceCalculator,
            photoNormalizationProvider,
            photoThumbnailProvider,
            sightingPhotoStore,
            new SubmissionCooldownProvider(timeProvider),
            NullLogger<SightingService>.Instance);
    }

    private void LatestCreateDate(Guid reporterId, DateTimeOffset createdAt)
    {
        sightingRepository
            .GetLatestCreateDateByReporterAsync(reporterId, Arg.Any<CancellationToken>())
            .Returns(createdAt.UtcDateTime);
    }

    private async Task AssertNoBoxQueryAsync()
    {
        await sightingRepository.DidNotReceive().ListInBoxAsync(
            Arg.Any<double>(),
            Arg.Any<double>(),
            Arg.Any<double>(),
            Arg.Any<double>(),
            Arg.Any<IReadOnlyCollection<SightingStatus>>(),
            Arg.Any<IReadOnlyCollection<SightingCriticality>>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    private async Task AssertNoPageQueryAsync()
    {
        await sightingRepository.DidNotReceive().ListPageInRadiusAsync(
            Arg.Any<GeoRadiusFilter>(),
            Arg.Any<IReadOnlyCollection<SightingStatus>>(),
            Arg.Any<IReadOnlyCollection<SightingCriticality>>(),
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    private void PageInRadius(SightingPage page)
    {
        sightingRepository
            .ListPageInRadiusAsync(
                Arg.Any<GeoRadiusFilter>(),
                Arg.Any<IReadOnlyCollection<SightingStatus>>(),
                Arg.Any<IReadOnlyCollection<SightingCriticality>>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(page);
    }

    private static SightingListQuery ListQuery()
    {
        return new(centreLatitude, centreLongitude, [], [], 1, SightingRules.ListDefaultPageSize);
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
                Arg.Any<IReadOnlyCollection<SightingCriticality>>(),
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

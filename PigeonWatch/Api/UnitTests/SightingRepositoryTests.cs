using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PigeonWatch.BusinessLogic.Services;
using PigeonWatch.BusinessObjects;
using PigeonWatch.Data;
using PigeonWatch.Data.Auditing;
using PigeonWatch.Data.Identity;
using PigeonWatch.Data.Mappers;
using PigeonWatch.Data.Repositories;

namespace PigeonWatch.UnitTests;

public sealed class SightingRepositoryTests : IDisposable
{
    private static readonly SightingStatus[] openStatuses =
        [SightingStatus.Spotted, SightingStatus.Caught, SightingStatus.AtTheVet, SightingStatus.InHomeCare, SightingStatus.AwaitingAdoption];

    private const double kilometersPerDegreeOfLatitude = 111.19508;

    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly UpperInvariantLookupNormalizer normalizer = new();
    private readonly ManualTimeProvider timeProvider = new(new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero));
    private readonly CancellationToken cancellationToken = TestContext.Current.CancellationToken;

    public SightingRepositoryTests()
    {
        connection.Open();
        using PigeonWatchDbContext db = CreateContext();
        db.Database.EnsureCreated();
    }

    [Fact]
    public async Task Create_returns_the_stored_sighting_with_the_reporter_display_name_and_utc_creation_time()
    {
        Guid reporterId = await CreateUserAsync("reporter@example.com", "Pidgey");

        Sighting? created = await CreateSightingAsync(NewSighting(reporterId, 52.2297, 21.0122, SightingStatus.Caught) with { Name = "Grey" });

        Assert.NotNull(created);
        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal("Grey", created.Name);
        Assert.Equal("Limping near the fountain", created.Description);
        Assert.Equal("Cannot fly", created.Behaviour);
        Assert.Equal(52.2297, created.Latitude);
        Assert.Equal(21.0122, created.Longitude);
        Assert.Equal(SightingStatus.Caught, created.Status);
        Assert.Equal(SightingCriticality.Urgent, created.Criticality);
        Assert.Equal("Pidgey", created.ReporterDisplayName);
        Assert.Equal(timeProvider.UtcNow.UtcDateTime, created.CreatedAt);
        Assert.Equal(DateTimeKind.Utc, created.CreatedAt.Kind);
    }

    [Fact]
    public async Task Create_returns_null_and_stores_nothing_when_the_reporter_does_not_exist()
    {
        Sighting? created = await CreateSightingAsync(NewSighting(Guid.NewGuid(), 52.0, 21.0, SightingStatus.Spotted));

        Assert.Null(created);

        await using PigeonWatchDbContext db = CreateContext();
        int count = await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM SIGHTING").SingleAsync(cancellationToken);

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task Get_by_id_returns_the_stored_sighting()
    {
        Guid reporterId = await CreateUserAsync("reporter@example.com", "Pidgey");
        Sighting created = (await CreateSightingAsync(NewSighting(reporterId, 52.0, 21.0, SightingStatus.Spotted)))!;

        await using PigeonWatchDbContext db = CreateContext();
        Sighting? loaded = await CreateRepository(db).GetByIdAsync(created.Id, cancellationToken);

        Assert.Equal(created, loaded);
    }

    [Fact]
    public async Task Get_by_id_returns_null_for_an_unknown_id()
    {
        await using PigeonWatchDbContext db = CreateContext();

        Assert.Null(await CreateRepository(db).GetByIdAsync(Guid.NewGuid(), cancellationToken));
    }

    [Fact]
    public async Task List_in_box_includes_sightings_on_every_edge_and_excludes_those_just_outside()
    {
        Guid reporterId = await CreateUserAsync("reporter@example.com", "Pidgey");
        Sighting minCorner = (await CreateSightingAsync(NewSighting(reporterId, 52.0, 21.0, SightingStatus.Spotted)))!;
        Sighting maxCorner = (await CreateSightingAsync(NewSighting(reporterId, 52.5, 21.5, SightingStatus.Spotted)))!;
        Sighting centre = (await CreateSightingAsync(NewSighting(reporterId, 52.25, 21.25, SightingStatus.Spotted)))!;
        await CreateSightingAsync(NewSighting(reporterId, 51.9999, 21.25, SightingStatus.Spotted));
        await CreateSightingAsync(NewSighting(reporterId, 52.5001, 21.25, SightingStatus.Spotted));
        await CreateSightingAsync(NewSighting(reporterId, 52.25, 20.9999, SightingStatus.Spotted));
        await CreateSightingAsync(NewSighting(reporterId, 52.25, 21.5001, SightingStatus.Spotted));

        await using PigeonWatchDbContext db = CreateContext();
        IReadOnlyList<Sighting> found = await CreateRepository(db).ListInBoxAsync(52.0, 52.5, 21.0, 21.5, openStatuses, [], 50, false, cancellationToken);

        Assert.Equal(
            new[] { minCorner.Id, maxCorner.Id, centre.Id }.Order(),
            found.Select(sighting => sighting.Id).Order());
    }

    [Fact]
    public async Task List_in_box_excludes_statuses_outside_the_given_set()
    {
        Guid reporterId = await CreateUserAsync("reporter@example.com", "Pidgey");
        List<Guid> openIds = [];

        foreach (SightingStatus status in Enum.GetValues<SightingStatus>())
        {
            Sighting created = (await CreateSightingAsync(NewSighting(reporterId, 52.25, 21.25, status)))!;

            if (openStatuses.Contains(status))
                openIds.Add(created.Id);
        }

        await using PigeonWatchDbContext db = CreateContext();
        IReadOnlyList<Sighting> found = await CreateRepository(db).ListInBoxAsync(52.0, 52.5, 21.0, 21.5, openStatuses, [], 50, false, cancellationToken);

        Assert.Equal(openIds.Order(), found.Select(sighting => sighting.Id).Order());
        Assert.All(found, sighting => Assert.Contains(sighting.Status, openStatuses));
    }

    [Fact]
    public async Task List_in_box_returns_the_newest_sightings_up_to_the_take()
    {
        Guid reporterId = await CreateUserAsync("reporter@example.com", "Pidgey");
        Sighting oldest = (await CreateSightingAsync(NewSighting(reporterId, 52.25, 21.25, SightingStatus.Spotted)))!;
        timeProvider.UtcNow = timeProvider.UtcNow.AddMinutes(5);
        Sighting middle = (await CreateSightingAsync(NewSighting(reporterId, 52.25, 21.25, SightingStatus.Spotted)))!;
        timeProvider.UtcNow = timeProvider.UtcNow.AddMinutes(5);
        Sighting newest = (await CreateSightingAsync(NewSighting(reporterId, 52.25, 21.25, SightingStatus.Spotted)))!;

        await using PigeonWatchDbContext db = CreateContext();
        ISightingRepository repository = CreateRepository(db);
        IReadOnlyList<Sighting> all = await repository.ListInBoxAsync(52.0, 52.5, 21.0, 21.5, openStatuses, [], 50, false, cancellationToken);
        IReadOnlyList<Sighting> firstTwo = await repository.ListInBoxAsync(52.0, 52.5, 21.0, 21.5, openStatuses, [], 2, false, cancellationToken);

        Assert.Equal([newest.Id, middle.Id, oldest.Id], all.Select(sighting => sighting.Id));
        Assert.Equal([newest.Id, middle.Id], firstTwo.Select(sighting => sighting.Id));
    }

    [Fact]
    public async Task List_in_box_with_criticalities_returns_only_sightings_of_those_criticalities()
    {
        Guid reporterId = await CreateUserAsync("reporter@example.com", "Pidgey");
        List<Guid> matchingIds = [];

        foreach (SightingCriticality criticality in Enum.GetValues<SightingCriticality>())
        {
            Sighting created = (await CreateSightingAsync(NewSighting(reporterId, 52.25, 21.25, SightingStatus.Spotted) with { Criticality = criticality }))!;

            if (criticality != SightingCriticality.Monitor)
                matchingIds.Add(created.Id);
        }

        await using PigeonWatchDbContext db = CreateContext();
        IReadOnlyList<Sighting> found = await CreateRepository(db).ListInBoxAsync(
            52.0,
            52.5,
            21.0,
            21.5,
            openStatuses,
            [SightingCriticality.NeedsHelpSoon, SightingCriticality.Urgent],
            50,
            false,
            cancellationToken);

        Assert.Equal(matchingIds.Order(), found.Select(sighting => sighting.Id).Order());
    }

    [Fact]
    public async Task List_in_box_with_empty_criticalities_applies_no_criticality_predicate()
    {
        Guid reporterId = await CreateUserAsync("reporter@example.com", "Pidgey");
        List<Guid> allIds = [];

        foreach (SightingCriticality criticality in Enum.GetValues<SightingCriticality>())
        {
            Sighting created = (await CreateSightingAsync(NewSighting(reporterId, 52.25, 21.25, SightingStatus.Spotted) with { Criticality = criticality }))!;
            allIds.Add(created.Id);
        }

        await using PigeonWatchDbContext db = CreateContext();
        IReadOnlyList<Sighting> found = await CreateRepository(db).ListInBoxAsync(52.0, 52.5, 21.0, 21.5, openStatuses, [], 50, false, cancellationToken);

        Assert.Equal(allIds.Order(), found.Select(sighting => sighting.Id).Order());
    }

    [Fact]
    public async Task List_in_box_applies_the_take_after_the_criticality_predicate()
    {
        Guid reporterId = await CreateUserAsync("reporter@example.com", "Pidgey");
        Sighting olderUrgent = (await CreateSightingAsync(NewSighting(reporterId, 52.25, 21.25, SightingStatus.Spotted)))!;
        timeProvider.UtcNow = timeProvider.UtcNow.AddMinutes(5);
        Sighting newerUrgent = (await CreateSightingAsync(NewSighting(reporterId, 52.25, 21.25, SightingStatus.Spotted)))!;
        timeProvider.UtcNow = timeProvider.UtcNow.AddMinutes(5);
        await CreateSightingAsync(NewSighting(reporterId, 52.25, 21.25, SightingStatus.Spotted) with { Criticality = SightingCriticality.Monitor });
        timeProvider.UtcNow = timeProvider.UtcNow.AddMinutes(5);
        await CreateSightingAsync(NewSighting(reporterId, 52.25, 21.25, SightingStatus.Spotted) with { Criticality = SightingCriticality.Monitor });

        await using PigeonWatchDbContext db = CreateContext();
        IReadOnlyList<Sighting> found = await CreateRepository(db).ListInBoxAsync(
            52.0,
            52.5,
            21.0,
            21.5,
            openStatuses,
            [SightingCriticality.Urgent],
            2,
            false,
            cancellationToken);

        Assert.Equal([newerUrgent.Id, olderUrgent.Id], found.Select(sighting => sighting.Id));
    }

    [Fact]
    public async Task List_page_in_radius_returns_sightings_inside_the_circle_and_excludes_those_outside()
    {
        Guid reporterId = await CreateUserAsync("reporter@example.com", "Pidgey");
        Sighting centre = (await CreateSightingAsync(NewSighting(reporterId, 52.0, 21.0, SightingStatus.Spotted)))!;
        Sighting justInside = (await CreateSightingAsync(NewSighting(reporterId, 52.0 + (9.9 / kilometersPerDegreeOfLatitude), 21.0, SightingStatus.Spotted)))!;
        await CreateSightingAsync(NewSighting(reporterId, 52.0 + (10.1 / kilometersPerDegreeOfLatitude), 21.0, SightingStatus.Spotted));
        await CreateSightingAsync(NewSighting(reporterId, 52.08, 21.13, SightingStatus.Spotted));

        await using PigeonWatchDbContext db = CreateContext();
        SightingPage page = await CreateRepository(db).ListPageInRadiusAsync(RadiusAroundCentre(), openStatuses, [], 0, 50, false, cancellationToken);

        Assert.Equal(new[] { centre.Id, justInside.Id }.Order(), page.Items.Select(sighting => sighting.Id).Order());
        Assert.Equal(2, page.TotalCount);
    }

    [Fact]
    public async Task List_page_in_radius_orders_by_criticality_descending_then_oldest_first_then_id()
    {
        Guid reporterId = await CreateUserAsync("reporter@example.com", "Pidgey");
        Sighting oldMonitor = (await CreateSightingAsync(NewSighting(reporterId, 52.0, 21.0, SightingStatus.Spotted) with { Criticality = SightingCriticality.Monitor }))!;
        Sighting oldUrgent = (await CreateSightingAsync(NewSighting(reporterId, 52.0, 21.0, SightingStatus.Spotted)))!;
        timeProvider.UtcNow = timeProvider.UtcNow.AddMinutes(5);
        Sighting needsHelpSoon = (await CreateSightingAsync(NewSighting(reporterId, 52.0, 21.0, SightingStatus.Spotted) with { Criticality = SightingCriticality.NeedsHelpSoon }))!;
        Sighting tiedUrgentFirst = (await CreateSightingAsync(NewSighting(reporterId, 52.0, 21.0, SightingStatus.Spotted)))!;
        Sighting tiedUrgentSecond = (await CreateSightingAsync(NewSighting(reporterId, 52.0, 21.0, SightingStatus.Spotted)))!;
        timeProvider.UtcNow = timeProvider.UtcNow.AddMinutes(5);
        Sighting newUrgent = (await CreateSightingAsync(NewSighting(reporterId, 52.0, 21.0, SightingStatus.Spotted)))!;
        Guid[] tiedUrgentIds = new[] { tiedUrgentFirst.Id, tiedUrgentSecond.Id }.Order().ToArray();

        await using PigeonWatchDbContext db = CreateContext();
        SightingPage page = await CreateRepository(db).ListPageInRadiusAsync(RadiusAroundCentre(), openStatuses, [], 0, 50, false, cancellationToken);

        Assert.Equal(
            [oldUrgent.Id, tiedUrgentIds[0], tiedUrgentIds[1], newUrgent.Id, needsHelpSoon.Id, oldMonitor.Id],
            page.Items.Select(sighting => sighting.Id));
    }

    [Fact]
    public async Task List_page_in_radius_counts_every_match_and_returns_one_page()
    {
        Guid reporterId = await CreateUserAsync("reporter@example.com", "Pidgey");
        List<Guid> ids = [];

        for (int index = 0; index < 5; index++)
        {
            timeProvider.UtcNow = timeProvider.UtcNow.AddMinutes(1);
            ids.Add((await CreateSightingAsync(NewSighting(reporterId, 52.0, 21.0, SightingStatus.Spotted)))!.Id);
        }

        await using PigeonWatchDbContext db = CreateContext();
        ISightingRepository repository = CreateRepository(db);
        SightingPage firstPage = await repository.ListPageInRadiusAsync(RadiusAroundCentre(), openStatuses, [], 0, 2, false, cancellationToken);
        SightingPage secondPage = await repository.ListPageInRadiusAsync(RadiusAroundCentre(), openStatuses, [], 2, 2, false, cancellationToken);
        SightingPage lastPage = await repository.ListPageInRadiusAsync(RadiusAroundCentre(), openStatuses, [], 4, 2, false, cancellationToken);

        Assert.Equal([ids[0], ids[1]], firstPage.Items.Select(sighting => sighting.Id));
        Assert.Equal([ids[2], ids[3]], secondPage.Items.Select(sighting => sighting.Id));
        Assert.Equal([ids[4]], lastPage.Items.Select(sighting => sighting.Id));
        Assert.All([firstPage, secondPage, lastPage], page => Assert.Equal(5, page.TotalCount));
        Assert.All(firstPage.Items, sighting => Assert.Equal("Pidgey", sighting.ReporterDisplayName));
    }

    [Fact]
    public async Task List_page_in_radius_past_the_end_returns_no_items_and_the_real_count()
    {
        Guid reporterId = await CreateUserAsync("reporter@example.com", "Pidgey");
        await CreateSightingAsync(NewSighting(reporterId, 52.0, 21.0, SightingStatus.Spotted));
        await CreateSightingAsync(NewSighting(reporterId, 52.0, 21.0, SightingStatus.Spotted));

        await using PigeonWatchDbContext db = CreateContext();
        SightingPage page = await CreateRepository(db).ListPageInRadiusAsync(RadiusAroundCentre(), openStatuses, [], 10, 10, false, cancellationToken);

        Assert.Empty(page.Items);
        Assert.Equal(2, page.TotalCount);
    }

    [Fact]
    public async Task List_page_in_radius_applies_the_status_and_criticality_predicates_before_the_count()
    {
        Guid reporterId = await CreateUserAsync("reporter@example.com", "Pidgey");
        SightingStatus[] requestedStatuses = [SightingStatus.Caught, SightingStatus.Adopted];
        List<Guid> matchingIds = [];

        foreach (SightingStatus status in Enum.GetValues<SightingStatus>())
        {
            foreach (SightingCriticality criticality in Enum.GetValues<SightingCriticality>())
            {
                Sighting created = (await CreateSightingAsync(NewSighting(reporterId, 52.0, 21.0, status) with { Criticality = criticality }))!;

                if (requestedStatuses.Contains(status) && criticality != SightingCriticality.Monitor)
                    matchingIds.Add(created.Id);
            }
        }

        await using PigeonWatchDbContext db = CreateContext();
        ISightingRepository repository = CreateRepository(db);
        SightingPage filtered = await repository.ListPageInRadiusAsync(
            RadiusAroundCentre(),
            requestedStatuses,
            [SightingCriticality.NeedsHelpSoon, SightingCriticality.Urgent],
            0,
            2,
            false,
            cancellationToken);
        SightingPage withoutCriticalities = await repository.ListPageInRadiusAsync(RadiusAroundCentre(), requestedStatuses, [], 0, 50, false, cancellationToken);

        Assert.Equal(4, filtered.TotalCount);
        Assert.Equal(2, filtered.Items.Count);
        Assert.All(filtered.Items, sighting => Assert.Contains(sighting.Id, matchingIds));
        Assert.Equal(6, withoutCriticalities.TotalCount);
        Assert.All(withoutCriticalities.Items, sighting => Assert.Contains(sighting.Status, requestedStatuses));
    }

    [Fact]
    public async Task List_in_box_excludes_removed_sightings_unless_they_are_included()
    {
        Guid reporterId = await CreateUserAsync("reporter@example.com", "Pidgey");
        Guid adminId = await CreateUserAsync("admin@example.com", "Admin");
        Sighting visible = (await CreateSightingAsync(NewSighting(reporterId, 52.25, 21.25, SightingStatus.Spotted)))!;
        Sighting removed = (await CreateSightingAsync(NewSighting(reporterId, 52.25, 21.25, SightingStatus.Spotted)))!;
        await RemoveSightingAsync(removed.Id, adminId, timeProvider.UtcNow.UtcDateTime);

        await using PigeonWatchDbContext db = CreateContext();
        ISightingRepository repository = CreateRepository(db);
        IReadOnlyList<Sighting> withoutRemoved = await repository.ListInBoxAsync(52.0, 52.5, 21.0, 21.5, openStatuses, [], 50, false, cancellationToken);
        IReadOnlyList<Sighting> withRemoved = await repository.ListInBoxAsync(52.0, 52.5, 21.0, 21.5, openStatuses, [], 50, true, cancellationToken);

        Assert.Equal([visible.Id], withoutRemoved.Select(sighting => sighting.Id));
        Assert.Equal(new[] { visible.Id, removed.Id }.Order(), withRemoved.Select(sighting => sighting.Id).Order());
        Assert.NotNull(withRemoved.Single(sighting => sighting.Id == removed.Id).RemovedAt);
    }

    [Fact]
    public async Task List_in_box_applies_the_take_after_excluding_removed_sightings()
    {
        Guid reporterId = await CreateUserAsync("reporter@example.com", "Pidgey");
        Guid adminId = await CreateUserAsync("admin@example.com", "Admin");
        Sighting visible = (await CreateSightingAsync(NewSighting(reporterId, 52.25, 21.25, SightingStatus.Spotted)))!;
        timeProvider.UtcNow = timeProvider.UtcNow.AddMinutes(5);
        Sighting removed = (await CreateSightingAsync(NewSighting(reporterId, 52.25, 21.25, SightingStatus.Spotted)))!;
        await RemoveSightingAsync(removed.Id, adminId, timeProvider.UtcNow.UtcDateTime);

        await using PigeonWatchDbContext db = CreateContext();
        IReadOnlyList<Sighting> found = await CreateRepository(db).ListInBoxAsync(52.0, 52.5, 21.0, 21.5, openStatuses, [], 1, false, cancellationToken);

        Assert.Equal([visible.Id], found.Select(sighting => sighting.Id));
    }

    [Fact]
    public async Task List_page_in_radius_excludes_removed_sightings_from_the_items_and_the_count_unless_they_are_included()
    {
        Guid reporterId = await CreateUserAsync("reporter@example.com", "Pidgey");
        Guid adminId = await CreateUserAsync("admin@example.com", "Admin");
        Sighting visible = (await CreateSightingAsync(NewSighting(reporterId, 52.0, 21.0, SightingStatus.Spotted)))!;
        Sighting removed = (await CreateSightingAsync(NewSighting(reporterId, 52.0, 21.0, SightingStatus.Spotted)))!;
        await RemoveSightingAsync(removed.Id, adminId, timeProvider.UtcNow.UtcDateTime);

        await using PigeonWatchDbContext db = CreateContext();
        ISightingRepository repository = CreateRepository(db);
        SightingPage withoutRemoved = await repository.ListPageInRadiusAsync(RadiusAroundCentre(), openStatuses, [], 0, 50, false, cancellationToken);
        SightingPage withRemoved = await repository.ListPageInRadiusAsync(RadiusAroundCentre(), openStatuses, [], 0, 50, true, cancellationToken);

        Assert.Equal([visible.Id], withoutRemoved.Items.Select(sighting => sighting.Id));
        Assert.Equal(1, withoutRemoved.TotalCount);
        Assert.Equal(new[] { visible.Id, removed.Id }.Order(), withRemoved.Items.Select(sighting => sighting.Id).Order());
        Assert.Equal(2, withRemoved.TotalCount);
    }

    [Fact]
    public async Task Get_access_returns_the_reporter_and_the_removal_state_or_null_for_an_unknown_id()
    {
        Guid reporterId = await CreateUserAsync("reporter@example.com", "Pidgey");
        Guid adminId = await CreateUserAsync("admin@example.com", "Admin");
        Sighting visible = (await CreateSightingAsync(NewSighting(reporterId, 52.0, 21.0, SightingStatus.Spotted)))!;
        Sighting removed = (await CreateSightingAsync(NewSighting(reporterId, 52.0, 21.0, SightingStatus.Spotted)))!;
        DateTime removedAt = timeProvider.UtcNow.UtcDateTime;
        await RemoveSightingAsync(removed.Id, adminId, removedAt);

        await using PigeonWatchDbContext db = CreateContext();
        ISightingRepository repository = CreateRepository(db);
        SightingAccess? visibleAccess = await repository.GetAccessAsync(visible.Id, cancellationToken);
        SightingAccess? removedAccess = await repository.GetAccessAsync(removed.Id, cancellationToken);
        SightingAccess? unknownAccess = await repository.GetAccessAsync(Guid.NewGuid(), cancellationToken);

        Assert.Equal(new SightingAccess(reporterId, null), visibleAccess);
        Assert.NotNull(removedAccess);
        Assert.Equal(reporterId, removedAccess.ReporterId);
        Assert.Equal(removedAt, removedAccess.RemovedAt);
        Assert.Null(unknownAccess);
    }

    [Fact]
    public async Task List_by_reporter_returns_only_that_reporters_sightings_newest_first_up_to_the_take()
    {
        Guid reporterId = await CreateUserAsync("reporter@example.com", "Pidgey");
        Guid otherId = await CreateUserAsync("other@example.com", "Feathers");
        Sighting oldest = (await CreateSightingAsync(NewSighting(reporterId, 52.0, 21.0, SightingStatus.Spotted)))!;
        timeProvider.UtcNow = timeProvider.UtcNow.AddMinutes(5);
        await CreateSightingAsync(NewSighting(otherId, 52.0, 21.0, SightingStatus.Spotted));
        timeProvider.UtcNow = timeProvider.UtcNow.AddMinutes(5);
        Sighting middle = (await CreateSightingAsync(NewSighting(reporterId, 52.0, 21.0, SightingStatus.Adopted)))!;
        timeProvider.UtcNow = timeProvider.UtcNow.AddMinutes(5);
        Sighting newest = (await CreateSightingAsync(NewSighting(reporterId, 52.0, 21.0, SightingStatus.Caught)))!;

        await using PigeonWatchDbContext db = CreateContext();
        ISightingRepository repository = CreateRepository(db);
        IReadOnlyList<Sighting> all = await repository.ListByReporterAsync(reporterId, 50, cancellationToken);
        IReadOnlyList<Sighting> firstTwo = await repository.ListByReporterAsync(reporterId, 2, cancellationToken);

        Assert.Equal([newest.Id, middle.Id, oldest.Id], all.Select(sighting => sighting.Id));
        Assert.Equal([newest.Id, middle.Id], firstTwo.Select(sighting => sighting.Id));
    }

    [Fact]
    public async Task List_by_reporter_returns_an_empty_list_when_the_reporter_has_no_sightings()
    {
        Guid reporterId = await CreateUserAsync("reporter@example.com", "Pidgey");

        await using PigeonWatchDbContext db = CreateContext();

        Assert.Empty(await CreateRepository(db).ListByReporterAsync(reporterId, 50, cancellationToken));
    }

    [Fact]
    public async Task Latest_create_date_by_reporter_returns_the_newest_utc_create_date_of_that_reporter()
    {
        Guid reporterId = await CreateUserAsync("reporter@example.com", "Pidgey");
        Guid otherId = await CreateUserAsync("other@example.com", "Feathers");
        await CreateSightingAsync(NewSighting(reporterId, 52.0, 21.0, SightingStatus.Spotted));
        timeProvider.UtcNow = timeProvider.UtcNow.AddSeconds(30);
        await CreateSightingAsync(NewSighting(reporterId, 52.0, 21.0, SightingStatus.Spotted));
        DateTime expected = timeProvider.UtcNow.UtcDateTime;
        timeProvider.UtcNow = timeProvider.UtcNow.AddMinutes(10);
        await CreateSightingAsync(NewSighting(otherId, 52.0, 21.0, SightingStatus.Spotted));

        await using PigeonWatchDbContext db = CreateContext();
        DateTime? latest = await CreateRepository(db).GetLatestCreateDateByReporterAsync(reporterId, cancellationToken);

        Assert.Equal(expected, latest);
        Assert.Equal(DateTimeKind.Utc, latest!.Value.Kind);
    }

    [Fact]
    public async Task Latest_create_date_by_reporter_returns_null_when_the_reporter_has_no_sightings()
    {
        Guid reporterId = await CreateUserAsync("reporter@example.com", "Pidgey");

        await using PigeonWatchDbContext db = CreateContext();

        Assert.Null(await CreateRepository(db).GetLatestCreateDateByReporterAsync(reporterId, cancellationToken));
    }

    [Fact]
    public async Task Other_user_engagement_is_false_when_nobody_commented_or_made_contact()
    {
        Guid reporterId = await CreateUserAsync("reporter@example.com", "Pidgey");
        Sighting sighting = (await CreateSightingAsync(NewSighting(reporterId, 52.0, 21.0, SightingStatus.Spotted)))!;

        Assert.False(await HasOtherUserEngagementAsync(sighting.Id, reporterId));
    }

    [Fact]
    public async Task Other_user_engagement_is_false_when_only_the_reporter_commented()
    {
        Guid reporterId = await CreateUserAsync("reporter@example.com", "Pidgey");
        Sighting sighting = (await CreateSightingAsync(NewSighting(reporterId, 52.0, 21.0, SightingStatus.Spotted)))!;
        await CreateCommentAsync(sighting.Id, reporterId);

        Assert.False(await HasOtherUserEngagementAsync(sighting.Id, reporterId));
    }

    [Fact]
    public async Task Other_user_engagement_is_true_when_another_user_has_a_live_comment()
    {
        Guid reporterId = await CreateUserAsync("reporter@example.com", "Pidgey");
        Guid bobId = await CreateUserAsync("bob@example.com", "Bob");
        Sighting sighting = (await CreateSightingAsync(NewSighting(reporterId, 52.0, 21.0, SightingStatus.Spotted)))!;
        await CreateCommentAsync(sighting.Id, bobId);

        Assert.True(await HasOtherUserEngagementAsync(sighting.Id, reporterId));
    }

    [Fact]
    public async Task Other_user_engagement_is_false_when_another_users_only_comment_is_soft_deleted()
    {
        Guid reporterId = await CreateUserAsync("reporter@example.com", "Pidgey");
        Guid bobId = await CreateUserAsync("bob@example.com", "Bob");
        Sighting sighting = (await CreateSightingAsync(NewSighting(reporterId, 52.0, 21.0, SightingStatus.Spotted)))!;
        SightingComment comment = await CreateCommentAsync(sighting.Id, bobId);

        await using (PigeonWatchDbContext db = CreateContext())
        {
            SightingComment? deleted = await new SightingCommentRepository(db, new SightingCommentMapper())
                .SoftDeleteAsync(sighting.Id, comment.Id, bobId, timeProvider.UtcNow.UtcDateTime, cancellationToken);

            Assert.NotNull(deleted);
        }

        Assert.False(await HasOtherUserEngagementAsync(sighting.Id, reporterId));
    }

    [Fact]
    public async Task Other_user_engagement_is_true_when_a_thread_exists()
    {
        Guid reporterId = await CreateUserAsync("reporter@example.com", "Pidgey");
        Guid bobId = await CreateUserAsync("bob@example.com", "Bob");
        Sighting sighting = (await CreateSightingAsync(NewSighting(reporterId, 52.0, 21.0, SightingStatus.Spotted)))!;

        await using (PigeonWatchDbContext db = CreateContext())
        {
            (SightingThread Thread, SightingMessage Message)? started = await new SightingThreadRepository(db, new SightingThreadMapper())
                .StartOrAppendAsync(sighting.Id, bobId, "I can help", timeProvider.UtcNow.UtcDateTime, cancellationToken);

            Assert.NotNull(started);
        }

        Assert.True(await HasOtherUserEngagementAsync(sighting.Id, reporterId));
    }

    [Fact]
    public async Task Other_user_engagement_ignores_comments_on_another_sighting()
    {
        Guid reporterId = await CreateUserAsync("reporter@example.com", "Pidgey");
        Guid bobId = await CreateUserAsync("bob@example.com", "Bob");
        Sighting sighting = (await CreateSightingAsync(NewSighting(reporterId, 52.0, 21.0, SightingStatus.Spotted)))!;
        Sighting other = (await CreateSightingAsync(NewSighting(reporterId, 52.0, 21.0, SightingStatus.Spotted)))!;
        await CreateCommentAsync(other.Id, bobId);

        Assert.False(await HasOtherUserEngagementAsync(sighting.Id, reporterId));
    }

    [Fact]
    public async Task Update_writes_the_editable_columns_and_keeps_the_location()
    {
        Guid reporterId = await CreateUserAsync("reporter@example.com", "Pidgey");
        Sighting sighting = (await CreateSightingAsync(NewSighting(reporterId, 52.2297, 21.0122, SightingStatus.Spotted)))!;
        SightingUpdate update = new(sighting.Id, reporterId, "Grey", "Limping badly", "Hides under a bench", SightingStatus.Caught, SightingCriticality.Monitor);
        string blobName = $"{Guid.NewGuid():D}.jpg";

        SightingUpdateOutcome? outcome = await UpdateSightingAsync(sighting.Id, reporterId, update, SightingPhotoChange.Replace, blobName);
        Sighting? loaded = await GetSightingAsync(sighting.Id);
        string? storedBlobName = await GetPhotoBlobNameAsync(sighting.Id);

        Assert.NotNull(outcome);
        Assert.Equal(loaded, outcome.Sighting);
        Assert.Equal("Grey", loaded!.Name);
        Assert.Equal("Limping badly", loaded.Description);
        Assert.Equal("Hides under a bench", loaded.Behaviour);
        Assert.Equal(SightingStatus.Caught, loaded.Status);
        Assert.Equal(SightingCriticality.Monitor, loaded.Criticality);
        Assert.Equal(52.2297, loaded.Latitude);
        Assert.Equal(21.0122, loaded.Longitude);
        Assert.Equal("Pidgey", loaded.ReporterDisplayName);
        Assert.Equal(sighting.CreatedAt, loaded.CreatedAt);
        Assert.True(loaded.HasPhoto);
        Assert.Equal(blobName, storedBlobName);
    }

    [Fact]
    public async Task Update_with_keep_leaves_a_photo_name_changed_after_the_callers_read_untouched()
    {
        Guid reporterId = await CreateUserAsync("reporter@example.com", "Pidgey");
        string readBlobName = $"{Guid.NewGuid():D}.jpg";
        string newerBlobName = $"{Guid.NewGuid():D}.jpg";
        Sighting sighting = (await CreateSightingAsync(NewSighting(reporterId, 52.0, 21.0, SightingStatus.Spotted) with { PhotoBlobName = readBlobName }))!;
        await SetPhotoBlobNameAsync(sighting.Id, newerBlobName);
        SightingUpdate update = new(sighting.Id, reporterId, "Grey", sighting.Description, sighting.Behaviour, SightingStatus.Caught, sighting.Criticality);

        SightingUpdateOutcome? outcome = await UpdateSightingAsync(sighting.Id, reporterId, update, SightingPhotoChange.Keep, null);

        Assert.NotNull(outcome);
        Assert.True(outcome.Sighting.HasPhoto);
        Assert.Equal(SightingStatus.Caught, outcome.Sighting.Status);
        Assert.Null(outcome.ReplacedPhotoBlobName);
        Assert.Equal(newerBlobName, await GetPhotoBlobNameAsync(sighting.Id));
    }

    [Fact]
    public async Task Update_with_replace_stores_the_new_name_and_returns_the_name_the_row_pointed_at()
    {
        Guid reporterId = await CreateUserAsync("reporter@example.com", "Pidgey");
        string readBlobName = $"{Guid.NewGuid():D}.jpg";
        string currentBlobName = $"{Guid.NewGuid():D}.jpg";
        string newBlobName = $"{Guid.NewGuid():D}.jpg";
        Sighting sighting = (await CreateSightingAsync(NewSighting(reporterId, 52.0, 21.0, SightingStatus.Spotted) with { PhotoBlobName = readBlobName }))!;
        await SetPhotoBlobNameAsync(sighting.Id, currentBlobName);
        SightingUpdate update = new(sighting.Id, reporterId, null, sighting.Description, sighting.Behaviour, sighting.Status, sighting.Criticality);

        SightingUpdateOutcome? outcome = await UpdateSightingAsync(sighting.Id, reporterId, update, SightingPhotoChange.Replace, newBlobName);

        Assert.NotNull(outcome);
        Assert.True(outcome.Sighting.HasPhoto);
        Assert.Equal(currentBlobName, outcome.ReplacedPhotoBlobName);
        Assert.Equal(newBlobName, await GetPhotoBlobNameAsync(sighting.Id));
    }

    [Fact]
    public async Task Update_with_replace_on_a_sighting_without_a_photo_returns_no_replaced_name()
    {
        Guid reporterId = await CreateUserAsync("reporter@example.com", "Pidgey");
        string newBlobName = $"{Guid.NewGuid():D}.jpg";
        Sighting sighting = (await CreateSightingAsync(NewSighting(reporterId, 52.0, 21.0, SightingStatus.Spotted)))!;
        SightingUpdate update = new(sighting.Id, reporterId, null, sighting.Description, sighting.Behaviour, sighting.Status, sighting.Criticality);

        SightingUpdateOutcome? outcome = await UpdateSightingAsync(sighting.Id, reporterId, update, SightingPhotoChange.Replace, newBlobName);

        Assert.NotNull(outcome);
        Assert.True(outcome.Sighting.HasPhoto);
        Assert.Null(outcome.ReplacedPhotoBlobName);
        Assert.Equal(newBlobName, await GetPhotoBlobNameAsync(sighting.Id));
    }

    [Fact]
    public async Task Update_with_remove_clears_the_photo_name_and_returns_the_name_the_row_pointed_at()
    {
        Guid reporterId = await CreateUserAsync("reporter@example.com", "Pidgey");
        string readBlobName = $"{Guid.NewGuid():D}.jpg";
        string currentBlobName = $"{Guid.NewGuid():D}.jpg";
        Sighting sighting = (await CreateSightingAsync(NewSighting(reporterId, 52.0, 21.0, SightingStatus.Spotted) with { PhotoBlobName = readBlobName }))!;
        await SetPhotoBlobNameAsync(sighting.Id, currentBlobName);
        SightingUpdate update = new(sighting.Id, reporterId, null, sighting.Description, sighting.Behaviour, sighting.Status, sighting.Criticality);

        SightingUpdateOutcome? outcome = await UpdateSightingAsync(sighting.Id, reporterId, update, SightingPhotoChange.Remove, null);

        Assert.NotNull(outcome);
        Assert.False(outcome.Sighting.HasPhoto);
        Assert.Equal(currentBlobName, outcome.ReplacedPhotoBlobName);
        Assert.Null(await GetPhotoBlobNameAsync(sighting.Id));
    }

    [Fact]
    public async Task Update_with_remove_on_a_sighting_without_a_photo_returns_no_replaced_name()
    {
        Guid reporterId = await CreateUserAsync("reporter@example.com", "Pidgey");
        Sighting sighting = (await CreateSightingAsync(NewSighting(reporterId, 52.0, 21.0, SightingStatus.Spotted)))!;
        SightingUpdate update = new(sighting.Id, reporterId, null, sighting.Description, sighting.Behaviour, sighting.Status, sighting.Criticality);

        SightingUpdateOutcome? outcome = await UpdateSightingAsync(sighting.Id, reporterId, update, SightingPhotoChange.Remove, null);

        Assert.NotNull(outcome);
        Assert.False(outcome.Sighting.HasPhoto);
        Assert.Null(outcome.ReplacedPhotoBlobName);
    }

    [Fact]
    public async Task Update_returns_null_and_writes_nothing_when_the_caller_is_not_the_reporter()
    {
        Guid reporterId = await CreateUserAsync("reporter@example.com", "Pidgey");
        Guid bobId = await CreateUserAsync("bob@example.com", "Bob");
        string blobName = $"{Guid.NewGuid():D}.jpg";
        Sighting sighting = (await CreateSightingAsync(NewSighting(reporterId, 52.0, 21.0, SightingStatus.Spotted) with { PhotoBlobName = blobName }))!;
        SightingUpdate update = new(sighting.Id, bobId, "Forged", "Forged description", "Forged behaviour", SightingStatus.Caught, SightingCriticality.Monitor);

        SightingUpdateOutcome? outcome = await UpdateSightingAsync(sighting.Id, bobId, update, SightingPhotoChange.Remove, null);

        Assert.Null(outcome);
        Assert.Equal(sighting, await GetSightingAsync(sighting.Id));
        Assert.Equal(blobName, await GetPhotoBlobNameAsync(sighting.Id));
    }

    [Fact]
    public async Task Update_returns_null_for_an_unknown_id()
    {
        Guid reporterId = await CreateUserAsync("reporter@example.com", "Pidgey");
        Guid unknownId = Guid.NewGuid();
        SightingUpdate update = new(unknownId, reporterId, null, "Limping", "Cannot fly", SightingStatus.Spotted, SightingCriticality.Monitor);

        SightingUpdateOutcome? outcome = await UpdateSightingAsync(unknownId, reporterId, update, SightingPhotoChange.Keep, null);

        Assert.Null(outcome);
    }

    [Fact]
    public async Task Remove_sets_the_utc_removal_time_and_the_remover()
    {
        Guid reporterId = await CreateUserAsync("reporter@example.com", "Pidgey");
        Guid adminId = await CreateUserAsync("admin@example.com", "Warden");
        Sighting sighting = (await CreateSightingAsync(NewSighting(reporterId, 52.0, 21.0, SightingStatus.Spotted)))!;
        DateTime removedAt = timeProvider.UtcNow.AddMinutes(3).UtcDateTime;

        Sighting? removed = await RemoveSightingAsync(sighting.Id, adminId, removedAt);
        Sighting? loaded = await GetSightingAsync(sighting.Id);

        Assert.NotNull(removed);
        Assert.Equal(removedAt, removed.RemovedAt);
        Assert.Equal(removed, loaded);
        Assert.Equal(removedAt, loaded!.RemovedAt);
        Assert.Equal(DateTimeKind.Utc, loaded.RemovedAt!.Value.Kind);
        Assert.Equal(adminId, await GetRemovedByAsync(sighting.Id));
    }

    [Fact]
    public async Task Remove_of_an_already_removed_sighting_keeps_the_first_removal()
    {
        Guid reporterId = await CreateUserAsync("reporter@example.com", "Pidgey");
        Guid firstAdminId = await CreateUserAsync("admin@example.com", "Warden");
        Guid secondAdminId = await CreateUserAsync("second.admin@example.com", "Keeper");
        Sighting sighting = (await CreateSightingAsync(NewSighting(reporterId, 52.0, 21.0, SightingStatus.Spotted)))!;
        DateTime firstRemovedAt = timeProvider.UtcNow.UtcDateTime;

        await RemoveSightingAsync(sighting.Id, firstAdminId, firstRemovedAt);
        Sighting? again = await RemoveSightingAsync(sighting.Id, secondAdminId, firstRemovedAt.AddHours(1));

        Assert.NotNull(again);
        Assert.Equal(firstRemovedAt, again.RemovedAt);
        Assert.Equal(firstRemovedAt, (await GetSightingAsync(sighting.Id))!.RemovedAt);
        Assert.Equal(firstAdminId, await GetRemovedByAsync(sighting.Id));
    }

    [Fact]
    public async Task Remove_returns_null_for_an_unknown_id()
    {
        Guid adminId = await CreateUserAsync("admin@example.com", "Warden");

        Assert.Null(await RemoveSightingAsync(Guid.NewGuid(), adminId, timeProvider.UtcNow.UtcDateTime));
    }

    [Fact]
    public async Task Restore_clears_the_removal_time_and_the_remover()
    {
        Guid reporterId = await CreateUserAsync("reporter@example.com", "Pidgey");
        Guid adminId = await CreateUserAsync("admin@example.com", "Warden");
        Sighting sighting = (await CreateSightingAsync(NewSighting(reporterId, 52.0, 21.0, SightingStatus.Spotted)))!;
        await RemoveSightingAsync(sighting.Id, adminId, timeProvider.UtcNow.UtcDateTime);

        Sighting? restored = await RestoreSightingAsync(sighting.Id);

        Assert.NotNull(restored);
        Assert.Null(restored.RemovedAt);
        Assert.Equal(sighting, await GetSightingAsync(sighting.Id));
        Assert.Null(await GetRemovedByAsync(sighting.Id));
    }

    [Fact]
    public async Task Restore_of_a_sighting_that_is_not_removed_returns_it_unchanged()
    {
        Guid reporterId = await CreateUserAsync("reporter@example.com", "Pidgey");
        Sighting sighting = (await CreateSightingAsync(NewSighting(reporterId, 52.0, 21.0, SightingStatus.Spotted)))!;

        Sighting? restored = await RestoreSightingAsync(sighting.Id);

        Assert.Equal(sighting, restored);
        Assert.Equal(sighting, await GetSightingAsync(sighting.Id));
    }

    [Fact]
    public async Task Restore_returns_null_for_an_unknown_id()
    {
        Assert.Null(await RestoreSightingAsync(Guid.NewGuid()));
    }

    public void Dispose()
    {
        connection.Dispose();
    }

    private async Task<Sighting?> RemoveSightingAsync(Guid sightingId, Guid removedBy, DateTime removedAt)
    {
        await using PigeonWatchDbContext db = CreateContext();

        return await CreateRepository(db).RemoveAsync(sightingId, removedBy, removedAt, cancellationToken);
    }

    private async Task<Sighting?> RestoreSightingAsync(Guid sightingId)
    {
        await using PigeonWatchDbContext db = CreateContext();

        return await CreateRepository(db).RestoreAsync(sightingId, cancellationToken);
    }

    private async Task<Guid?> GetRemovedByAsync(Guid sightingId)
    {
        await using PigeonWatchDbContext db = CreateContext();
        Guid? removedBy = await db.Sightings
            .Where(e => e.Id == sightingId)
            .Select(e => e.RemovedBy)
            .SingleAsync(cancellationToken);

        return removedBy;
    }

    private static GeoRadiusFilter RadiusAroundCentre()
    {
        return new GeoDistanceCalculator().GetRadiusFilter(52.0, 21.0, SightingRules.NearbyRadiusKilometers);
    }

    private static NewSighting NewSighting(Guid reporterId, double latitude, double longitude, SightingStatus status)
    {
        return new NewSighting(reporterId, null, "Limping near the fountain", "Cannot fly", latitude, longitude, status, SightingCriticality.Urgent);
    }

    private async Task<Sighting?> CreateSightingAsync(NewSighting sighting)
    {
        await using PigeonWatchDbContext db = CreateContext();

        return await CreateRepository(db).CreateAsync(sighting, cancellationToken);
    }

    private async Task<SightingComment> CreateCommentAsync(Guid sightingId, Guid authorId)
    {
        await using PigeonWatchDbContext db = CreateContext();
        SightingComment? comment = await new SightingCommentRepository(db, new SightingCommentMapper())
            .CreateAsync(new NewSightingComment(sightingId, authorId, "Seen it too"), cancellationToken);

        Assert.NotNull(comment);

        return comment;
    }

    private async Task<SightingUpdateOutcome?> UpdateSightingAsync(
        Guid sightingId,
        Guid reporterId,
        SightingUpdate update,
        SightingPhotoChange photoChange,
        string? newPhotoBlobName)
    {
        await using PigeonWatchDbContext db = CreateContext();

        return await CreateRepository(db).UpdateAsync(sightingId, reporterId, update, photoChange, newPhotoBlobName, cancellationToken);
    }

    private async Task SetPhotoBlobNameAsync(Guid sightingId, string photoBlobName)
    {
        await using PigeonWatchDbContext db = CreateContext();
        int affected = await db.Sightings
            .Where(e => e.Id == sightingId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(e => e.PhotoBlobName, photoBlobName), cancellationToken);

        Assert.Equal(1, affected);
    }

    private async Task<Sighting?> GetSightingAsync(Guid sightingId)
    {
        await using PigeonWatchDbContext db = CreateContext();

        return await CreateRepository(db).GetByIdAsync(sightingId, cancellationToken);
    }

    private async Task<string?> GetPhotoBlobNameAsync(Guid sightingId)
    {
        await using PigeonWatchDbContext db = CreateContext();

        return await CreateRepository(db).GetPhotoBlobNameAsync(sightingId, cancellationToken);
    }

    private async Task<bool> HasOtherUserEngagementAsync(Guid sightingId, Guid reporterId)
    {
        await using PigeonWatchDbContext db = CreateContext();

        return await CreateRepository(db).HasOtherUserEngagementAsync(sightingId, reporterId, cancellationToken);
    }

    private async Task<Guid> CreateUserAsync(string email, string displayName)
    {
        ApplicationUser user = new()
        {
            Email = email,
            NormalizedEmail = normalizer.NormalizeEmail(email)!,
            UserName = email,
            NormalizedUserName = normalizer.NormalizeName(email)!,
            DisplayName = displayName,
            PasswordHash = "hash",
            SecurityStamp = Guid.NewGuid().ToString()
        };

        await using PigeonWatchDbContext db = CreateContext();
        PigeonWatchUserStore store = new(db, new UserAccountMapper(), normalizer, new IdentityErrorDescriber(), NullLogger<PigeonWatchUserStore>.Instance);
        IdentityResult result = await store.CreateAsync(user, cancellationToken);

        Assert.True(result.Succeeded);

        return user.Id;
    }

    private ISightingRepository CreateRepository(PigeonWatchDbContext db)
    {
        return new SightingRepository(db, new SightingMapper());
    }

    private PigeonWatchDbContext CreateContext()
    {
        DbContextOptions<PigeonWatchDbContext> options = new DbContextOptionsBuilder<PigeonWatchDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(new AuditSaveChangesInterceptor(
                new HttpContextCurrentUserProvider(new HttpContextAccessor(), NullLogger<HttpContextCurrentUserProvider>.Instance),
                timeProvider))
            .Options;

        return new PigeonWatchDbContext(options);
    }
}

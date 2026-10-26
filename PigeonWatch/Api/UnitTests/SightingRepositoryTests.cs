using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
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
        IReadOnlyList<Sighting> found = await CreateRepository(db).ListInBoxAsync(52.0, 52.5, 21.0, 21.5, openStatuses, 50, cancellationToken);

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
        IReadOnlyList<Sighting> found = await CreateRepository(db).ListInBoxAsync(52.0, 52.5, 21.0, 21.5, openStatuses, 50, cancellationToken);

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
        IReadOnlyList<Sighting> all = await repository.ListInBoxAsync(52.0, 52.5, 21.0, 21.5, openStatuses, 50, cancellationToken);
        IReadOnlyList<Sighting> firstTwo = await repository.ListInBoxAsync(52.0, 52.5, 21.0, 21.5, openStatuses, 2, cancellationToken);

        Assert.Equal([newest.Id, middle.Id, oldest.Id], all.Select(sighting => sighting.Id));
        Assert.Equal([newest.Id, middle.Id], firstTwo.Select(sighting => sighting.Id));
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

    public void Dispose()
    {
        connection.Dispose();
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

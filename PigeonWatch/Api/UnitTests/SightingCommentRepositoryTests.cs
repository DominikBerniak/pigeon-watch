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

public sealed class SightingCommentRepositoryTests : IDisposable
{
    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly UpperInvariantLookupNormalizer normalizer = new();
    private readonly ManualTimeProvider timeProvider = new(new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero));
    private readonly CancellationToken cancellationToken = TestContext.Current.CancellationToken;

    public SightingCommentRepositoryTests()
    {
        connection.Open();
        using PigeonWatchDbContext db = CreateContext();
        db.Database.EnsureCreated();
    }

    [Fact]
    public async Task Create_returns_the_stored_comment_with_the_author_display_name_and_utc_creation_time()
    {
        Guid authorId = await CreateUserAsync("author@example.com", "Pidgey");
        Guid sightingId = await CreateSightingAsync(authorId);

        SightingComment? created = await CreateCommentAsync(sightingId, authorId, "Still there");

        Assert.NotNull(created);
        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal(sightingId, created.SightingId);
        Assert.Equal(authorId, created.AuthorId);
        Assert.Equal("Pidgey", created.AuthorDisplayName);
        Assert.Equal("Still there", created.Text);
        Assert.Equal(timeProvider.UtcNow.UtcDateTime, created.CreatedAt);
        Assert.Equal(DateTimeKind.Utc, created.CreatedAt.Kind);
        Assert.Null(created.EditedAt);
        Assert.Null(created.DeletedAt);
    }

    [Fact]
    public async Task Create_returns_null_and_stores_nothing_when_the_author_does_not_exist()
    {
        Guid reporterId = await CreateUserAsync("reporter@example.com", "Pidgey");
        Guid sightingId = await CreateSightingAsync(reporterId);

        SightingComment? created = await CreateCommentAsync(sightingId, Guid.NewGuid(), "Still there");

        Assert.Null(created);

        await using PigeonWatchDbContext db = CreateContext();
        int count = await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM SIGHTING_COMMENT").SingleAsync(cancellationToken);

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task Get_returns_the_comment_only_within_its_sighting()
    {
        Guid authorId = await CreateUserAsync("author@example.com", "Pidgey");
        Guid sightingId = await CreateSightingAsync(authorId);
        Guid otherSightingId = await CreateSightingAsync(authorId);
        SightingComment created = (await CreateCommentAsync(sightingId, authorId, "Still there"))!;

        await using PigeonWatchDbContext db = CreateContext();
        ISightingCommentRepository repository = CreateRepository(db);

        Assert.Equal(created, await repository.GetAsync(sightingId, created.Id, cancellationToken));
        Assert.Null(await repository.GetAsync(otherSightingId, created.Id, cancellationToken));
        Assert.Null(await repository.GetAsync(sightingId, Guid.NewGuid(), cancellationToken));
    }

    [Fact]
    public async Task Get_returns_a_soft_deleted_comment()
    {
        Guid authorId = await CreateUserAsync("author@example.com", "Pidgey");
        Guid sightingId = await CreateSightingAsync(authorId);
        SightingComment created = (await CreateCommentAsync(sightingId, authorId, "Still there"))!;
        DateTime deletedAt = timeProvider.UtcNow.UtcDateTime.AddMinutes(1);
        await SoftDeleteAsync(created.Id, deletedAt);

        await using PigeonWatchDbContext db = CreateContext();
        SightingComment? loaded = await CreateRepository(db).GetAsync(sightingId, created.Id, cancellationToken);

        Assert.NotNull(loaded);
        Assert.Equal(deletedAt, loaded.DeletedAt);
        Assert.Equal(DateTimeKind.Utc, loaded.DeletedAt!.Value.Kind);
    }

    [Fact]
    public async Task List_page_returns_the_newest_first_with_an_id_tiebreak()
    {
        Guid authorId = await CreateUserAsync("author@example.com", "Pidgey");
        Guid sightingId = await CreateSightingAsync(authorId);
        SightingComment tiedFirst = (await CreateCommentAsync(sightingId, authorId, "One"))!;
        SightingComment tiedSecond = (await CreateCommentAsync(sightingId, authorId, "Two"))!;
        timeProvider.UtcNow = timeProvider.UtcNow.AddMinutes(5);
        SightingComment newest = (await CreateCommentAsync(sightingId, authorId, "Three"))!;
        Guid[] tiedIds = new[] { tiedFirst.Id, tiedSecond.Id }.OrderDescending().ToArray();

        await using PigeonWatchDbContext db = CreateContext();
        SightingCommentPage page = await CreateRepository(db).ListPageAsync(sightingId, 1, SightingCommentRules.PageSize, cancellationToken);

        Assert.Equal([newest.Id, tiedIds[0], tiedIds[1]], page.Items.Select(comment => comment.Id));
        Assert.Equal(3, page.TotalCount);
        Assert.All(page.Items, comment => Assert.Equal("Pidgey", comment.AuthorDisplayName));
    }

    [Fact]
    public async Task List_page_counts_and_returns_deleted_comments()
    {
        Guid authorId = await CreateUserAsync("author@example.com", "Pidgey");
        Guid sightingId = await CreateSightingAsync(authorId);
        SightingComment kept = (await CreateCommentAsync(sightingId, authorId, "Kept"))!;
        timeProvider.UtcNow = timeProvider.UtcNow.AddMinutes(1);
        SightingComment deleted = (await CreateCommentAsync(sightingId, authorId, "Deleted"))!;
        await SoftDeleteAsync(deleted.Id, timeProvider.UtcNow.UtcDateTime.AddMinutes(1));

        await using PigeonWatchDbContext db = CreateContext();
        SightingCommentPage page = await CreateRepository(db).ListPageAsync(sightingId, 1, SightingCommentRules.PageSize, cancellationToken);

        Assert.Equal(2, page.TotalCount);
        Assert.Equal([deleted.Id, kept.Id], page.Items.Select(comment => comment.Id));
        Assert.NotNull(page.Items[0].DeletedAt);
        Assert.Null(page.Items[1].DeletedAt);
    }

    [Fact]
    public async Task List_page_returns_only_the_comments_of_that_sighting()
    {
        Guid authorId = await CreateUserAsync("author@example.com", "Pidgey");
        Guid sightingId = await CreateSightingAsync(authorId);
        Guid otherSightingId = await CreateSightingAsync(authorId);
        SightingComment own = (await CreateCommentAsync(sightingId, authorId, "Here"))!;
        await CreateCommentAsync(otherSightingId, authorId, "Elsewhere");

        await using PigeonWatchDbContext db = CreateContext();
        SightingCommentPage page = await CreateRepository(db).ListPageAsync(sightingId, 1, SightingCommentRules.PageSize, cancellationToken);

        Assert.Equal([own.Id], page.Items.Select(comment => comment.Id));
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task List_page_skips_earlier_pages_and_a_page_past_the_end_is_empty_with_the_real_count()
    {
        Guid authorId = await CreateUserAsync("author@example.com", "Pidgey");
        Guid sightingId = await CreateSightingAsync(authorId);
        List<Guid> newestFirst = [];

        for (int index = 0; index < 5; index++)
        {
            SightingComment created = (await CreateCommentAsync(sightingId, authorId, $"Comment {index}"))!;
            newestFirst.Insert(0, created.Id);
            timeProvider.UtcNow = timeProvider.UtcNow.AddMinutes(1);
        }

        await using PigeonWatchDbContext db = CreateContext();
        ISightingCommentRepository repository = CreateRepository(db);
        SightingCommentPage second = await repository.ListPageAsync(sightingId, 2, 2, cancellationToken);
        SightingCommentPage third = await repository.ListPageAsync(sightingId, 3, 2, cancellationToken);
        SightingCommentPage pastTheEnd = await repository.ListPageAsync(sightingId, 4, 2, cancellationToken);

        Assert.Equal(newestFirst.Skip(2).Take(2), second.Items.Select(comment => comment.Id));
        Assert.Equal(newestFirst.Skip(4), third.Items.Select(comment => comment.Id));
        Assert.Empty(pastTheEnd.Items);
        Assert.Equal(5, pastTheEnd.TotalCount);
    }

    [Fact]
    public async Task Update_text_sets_the_text_and_edited_time_and_keeps_the_creation_time()
    {
        Guid authorId = await CreateUserAsync("author@example.com", "Pidgey");
        Guid sightingId = await CreateSightingAsync(authorId);
        SightingComment created = (await CreateCommentAsync(sightingId, authorId, "Still there"))!;
        timeProvider.UtcNow = timeProvider.UtcNow.AddMinutes(2);
        DateTime editedAt = timeProvider.UtcNow.UtcDateTime;

        SightingComment? updated = await UpdateTextAsync(created.Id, "Gone now", editedAt);

        Assert.NotNull(updated);
        Assert.Equal("Gone now", updated.Text);
        Assert.Equal(editedAt, updated.EditedAt);
        Assert.Equal(DateTimeKind.Utc, updated.EditedAt!.Value.Kind);
        Assert.Equal(created.CreatedAt, updated.CreatedAt);
        Assert.Null(updated.DeletedAt);
        Assert.Equal("Pidgey", updated.AuthorDisplayName);

        await using PigeonWatchDbContext db = CreateContext();

        Assert.Equal(updated, await CreateRepository(db).GetAsync(sightingId, created.Id, cancellationToken));
    }

    [Fact]
    public async Task Soft_delete_sets_the_deleted_time_and_not_the_edited_time()
    {
        Guid authorId = await CreateUserAsync("author@example.com", "Pidgey");
        Guid sightingId = await CreateSightingAsync(authorId);
        SightingComment created = (await CreateCommentAsync(sightingId, authorId, "Still there"))!;
        DateTime deletedAt = timeProvider.UtcNow.UtcDateTime.AddMinutes(3);

        SightingComment? deleted = await SoftDeleteAsync(created.Id, deletedAt);

        Assert.NotNull(deleted);
        Assert.Equal(deletedAt, deleted.DeletedAt);
        Assert.Null(deleted.EditedAt);
        Assert.Equal("Still there", deleted.Text);
    }

    [Fact]
    public async Task Update_text_and_soft_delete_return_null_for_an_unknown_comment()
    {
        await using PigeonWatchDbContext db = CreateContext();
        ISightingCommentRepository repository = CreateRepository(db);

        Assert.Null(await repository.UpdateTextAsync(Guid.NewGuid(), "Text", timeProvider.UtcNow.UtcDateTime, cancellationToken));
        Assert.Null(await repository.SoftDeleteAsync(Guid.NewGuid(), timeProvider.UtcNow.UtcDateTime, cancellationToken));
    }

    [Fact]
    public async Task Latest_submission_is_null_when_the_author_has_no_comments()
    {
        Guid authorId = await CreateUserAsync("author@example.com", "Pidgey");

        await using PigeonWatchDbContext db = CreateContext();

        Assert.Null(await CreateRepository(db).GetLatestSubmissionDateByAuthorAsync(authorId, cancellationToken));
    }

    [Fact]
    public async Task Latest_submission_is_the_newest_utc_create_date_of_that_author()
    {
        Guid authorId = await CreateUserAsync("author@example.com", "Pidgey");
        Guid otherId = await CreateUserAsync("other@example.com", "Dove");
        Guid sightingId = await CreateSightingAsync(authorId);
        await CreateCommentAsync(sightingId, authorId, "First");
        timeProvider.UtcNow = timeProvider.UtcNow.AddMinutes(5);
        await CreateCommentAsync(sightingId, authorId, "Second");
        DateTime expected = timeProvider.UtcNow.UtcDateTime;
        timeProvider.UtcNow = timeProvider.UtcNow.AddMinutes(5);
        await CreateCommentAsync(sightingId, otherId, "Someone else");

        await using PigeonWatchDbContext db = CreateContext();
        DateTime? latest = await CreateRepository(db).GetLatestSubmissionDateByAuthorAsync(authorId, cancellationToken);

        Assert.Equal(expected, latest);
        Assert.Equal(DateTimeKind.Utc, latest!.Value.Kind);
    }

    [Fact]
    public async Task Latest_submission_considers_an_edit_newer_than_every_creation()
    {
        Guid authorId = await CreateUserAsync("author@example.com", "Pidgey");
        Guid sightingId = await CreateSightingAsync(authorId);
        SightingComment older = (await CreateCommentAsync(sightingId, authorId, "First"))!;
        timeProvider.UtcNow = timeProvider.UtcNow.AddMinutes(5);
        await CreateCommentAsync(sightingId, authorId, "Second");
        DateTime editedAt = timeProvider.UtcNow.UtcDateTime.AddMinutes(5);
        await UpdateTextAsync(older.Id, "First, edited", editedAt);

        await using PigeonWatchDbContext db = CreateContext();
        DateTime? latest = await CreateRepository(db).GetLatestSubmissionDateByAuthorAsync(authorId, cancellationToken);

        Assert.Equal(editedAt, latest);
        Assert.Equal(DateTimeKind.Utc, latest!.Value.Kind);
    }

    [Fact]
    public async Task Latest_submission_keeps_the_newest_creation_when_every_edit_is_older()
    {
        Guid authorId = await CreateUserAsync("author@example.com", "Pidgey");
        Guid sightingId = await CreateSightingAsync(authorId);
        SightingComment older = (await CreateCommentAsync(sightingId, authorId, "First"))!;
        await UpdateTextAsync(older.Id, "First, edited", timeProvider.UtcNow.UtcDateTime.AddMinutes(1));
        timeProvider.UtcNow = timeProvider.UtcNow.AddMinutes(5);
        await CreateCommentAsync(sightingId, authorId, "Second");
        DateTime expected = timeProvider.UtcNow.UtcDateTime;

        await using PigeonWatchDbContext db = CreateContext();

        Assert.Equal(expected, await CreateRepository(db).GetLatestSubmissionDateByAuthorAsync(authorId, cancellationToken));
    }

    [Fact]
    public async Task Latest_submission_includes_deleted_comments_and_ignores_the_deleted_time()
    {
        Guid authorId = await CreateUserAsync("author@example.com", "Pidgey");
        Guid sightingId = await CreateSightingAsync(authorId);
        SightingComment created = (await CreateCommentAsync(sightingId, authorId, "Gone soon"))!;
        await SoftDeleteAsync(created.Id, timeProvider.UtcNow.UtcDateTime.AddMinutes(10));

        await using PigeonWatchDbContext db = CreateContext();
        DateTime? latest = await CreateRepository(db).GetLatestSubmissionDateByAuthorAsync(authorId, cancellationToken);

        Assert.Equal(created.CreatedAt, latest);
    }

    [Fact]
    public async Task Latest_submission_includes_the_edit_of_a_deleted_comment()
    {
        Guid authorId = await CreateUserAsync("author@example.com", "Pidgey");
        Guid sightingId = await CreateSightingAsync(authorId);
        SightingComment created = (await CreateCommentAsync(sightingId, authorId, "Edited, then deleted"))!;
        DateTime editedAt = timeProvider.UtcNow.UtcDateTime.AddMinutes(2);
        await UpdateTextAsync(created.Id, "Edited", editedAt);
        await SoftDeleteAsync(created.Id, timeProvider.UtcNow.UtcDateTime.AddMinutes(3));

        await using PigeonWatchDbContext db = CreateContext();

        Assert.Equal(editedAt, await CreateRepository(db).GetLatestSubmissionDateByAuthorAsync(authorId, cancellationToken));
    }

    [Fact]
    public async Task Sighting_exists_is_true_only_for_a_stored_sighting()
    {
        Guid authorId = await CreateUserAsync("author@example.com", "Pidgey");
        Guid sightingId = await CreateSightingAsync(authorId);

        await using PigeonWatchDbContext db = CreateContext();
        ISightingCommentRepository repository = CreateRepository(db);

        Assert.True(await repository.SightingExistsAsync(sightingId, cancellationToken));
        Assert.False(await repository.SightingExistsAsync(Guid.NewGuid(), cancellationToken));
    }

    public void Dispose()
    {
        connection.Dispose();
    }

    private async Task<SightingComment?> CreateCommentAsync(Guid sightingId, Guid authorId, string text)
    {
        await using PigeonWatchDbContext db = CreateContext();

        return await CreateRepository(db).CreateAsync(new NewSightingComment(sightingId, authorId, text), cancellationToken);
    }

    private async Task<SightingComment?> UpdateTextAsync(Guid commentId, string text, DateTime editedAt)
    {
        await using PigeonWatchDbContext db = CreateContext();

        return await CreateRepository(db).UpdateTextAsync(commentId, text, editedAt, cancellationToken);
    }

    private async Task<SightingComment?> SoftDeleteAsync(Guid commentId, DateTime deletedAt)
    {
        await using PigeonWatchDbContext db = CreateContext();

        return await CreateRepository(db).SoftDeleteAsync(commentId, deletedAt, cancellationToken);
    }

    private async Task<Guid> CreateSightingAsync(Guid reporterId)
    {
        await using PigeonWatchDbContext db = CreateContext();
        ISightingRepository repository = new SightingRepository(db, new SightingMapper());
        Sighting? created = await repository.CreateAsync(
            new NewSighting(reporterId, null, "Limping near the fountain", "Cannot fly", 52.0, 21.0, SightingStatus.Spotted, SightingCriticality.Urgent),
            cancellationToken);

        Assert.NotNull(created);

        return created.Id;
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

    private ISightingCommentRepository CreateRepository(PigeonWatchDbContext db)
    {
        return new SightingCommentRepository(db, new SightingCommentMapper());
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

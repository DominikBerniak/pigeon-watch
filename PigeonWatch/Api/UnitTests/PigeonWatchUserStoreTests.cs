using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PigeonWatch.BusinessObjects;
using PigeonWatch.Data;
using PigeonWatch.Data.Auditing;
using PigeonWatch.Data.Identity;
using PigeonWatch.Data.Mappers;

namespace PigeonWatch.UnitTests;

public sealed class PigeonWatchUserStoreTests : IDisposable
{
    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly UpperInvariantLookupNormalizer normalizer = new();
    private readonly CancellationToken cancellationToken = TestContext.Current.CancellationToken;

    public PigeonWatchUserStoreTests()
    {
        connection.Open();
        using PigeonWatchDbContext db = CreateContext();
        db.Database.EnsureCreated();
    }

    [Fact]
    public async Task Create_assigns_the_generated_id_and_finds_the_user_by_normalized_email_and_user_name()
    {
        ApplicationUser user = NewUser("user@example.com", "Pidgey");

        IdentityResult result = await CreateAsync(user);

        Assert.True(result.Succeeded);
        Assert.NotEqual(Guid.Empty, user.Id);

        await using PigeonWatchDbContext db = CreateContext();
        PigeonWatchUserStore store = CreateStore(db);
        ApplicationUser? byEmail = await store.FindByEmailAsync("USER@EXAMPLE.COM", cancellationToken);
        ApplicationUser? byName = await store.FindByNameAsync("USER@EXAMPLE.COM", cancellationToken);
        ApplicationUser? byId = await store.FindByIdAsync(user.Id.ToString(), cancellationToken);

        Assert.Equal(user.Id, byEmail?.Id);
        Assert.Equal(user.Id, byName?.Id);
        Assert.Equal("Pidgey", byId?.DisplayName);
        Assert.Null(await store.FindByEmailAsync("user@example.com", cancellationToken));
    }

    [Fact]
    public async Task Create_stores_the_normalized_display_name()
    {
        await CreateAsync(NewUser("user@example.com", "Pidgey"));

        await using PigeonWatchDbContext db = CreateContext();
        string normalizedDisplayName = await db.Database
            .SqlQueryRaw<string>("SELECT NORMALIZED_DISPLAY_NAME AS Value FROM USER_ACCOUNT")
            .SingleAsync(cancellationToken);

        Assert.Equal("PIDGEY", normalizedDisplayName);
    }

    [Fact]
    public async Task Create_rejects_a_display_name_taken_in_a_different_case()
    {
        await CreateAsync(NewUser("first@example.com", "Pidgey"));

        IdentityResult result = await CreateAsync(NewUser("second@example.com", "pIDGEY"));

        Assert.False(result.Succeeded);
        Assert.Equal([AccountErrorCodes.DuplicateDisplayName], result.Errors.Select(error => error.Code));
    }

    [Fact]
    public async Task Update_rejects_a_display_name_taken_by_another_user()
    {
        await CreateAsync(NewUser("first@example.com", "Pidgey"));
        ApplicationUser second = NewUser("second@example.com", "Feathers");
        await CreateAsync(second);

        await using PigeonWatchDbContext db = CreateContext();
        PigeonWatchUserStore store = CreateStore(db);
        ApplicationUser loaded = (await store.FindByIdAsync(second.Id.ToString(), cancellationToken))!;
        loaded.DisplayName = "PIDGEY";
        IdentityResult result = await store.UpdateAsync(loaded, cancellationToken);

        Assert.Equal([AccountErrorCodes.DuplicateDisplayName], result.Errors.Select(error => error.Code));
    }

    [Fact]
    public async Task Update_with_a_stale_concurrency_stamp_returns_concurrency_failure()
    {
        ApplicationUser user = NewUser("user@example.com", "Pidgey");
        await CreateAsync(user);

        await using PigeonWatchDbContext firstDb = CreateContext();
        await using PigeonWatchDbContext secondDb = CreateContext();
        PigeonWatchUserStore firstStore = CreateStore(firstDb);
        PigeonWatchUserStore secondStore = CreateStore(secondDb);
        ApplicationUser firstCopy = (await firstStore.FindByIdAsync(user.Id.ToString(), cancellationToken))!;
        ApplicationUser staleCopy = (await secondStore.FindByIdAsync(user.Id.ToString(), cancellationToken))!;

        firstCopy.AccessFailedCount = 1;
        IdentityResult firstResult = await firstStore.UpdateAsync(firstCopy, cancellationToken);
        staleCopy.AccessFailedCount = 2;
        IdentityResult staleResult = await secondStore.UpdateAsync(staleCopy, cancellationToken);

        Assert.True(firstResult.Succeeded);
        Assert.NotEqual(user.ConcurrencyStamp, firstCopy.ConcurrencyStamp);
        Assert.Equal([new IdentityErrorDescriber().ConcurrencyFailure().Code], staleResult.Errors.Select(error => error.Code));
    }

    [Fact]
    public async Task Lockout_fields_are_persisted()
    {
        ApplicationUser user = NewUser("user@example.com", "Pidgey");
        await CreateAsync(user);
        DateTimeOffset lockoutEnd = new(2026, 10, 4, 12, 30, 0, TimeSpan.Zero);

        await using (PigeonWatchDbContext db = CreateContext())
        {
            PigeonWatchUserStore store = CreateStore(db);
            ApplicationUser loaded = (await store.FindByIdAsync(user.Id.ToString(), cancellationToken))!;
            await store.SetLockoutEnabledAsync(loaded, true, cancellationToken);
            await store.SetLockoutEndDateAsync(loaded, lockoutEnd, cancellationToken);
            await store.IncrementAccessFailedCountAsync(loaded, cancellationToken);
            await store.IncrementAccessFailedCountAsync(loaded, cancellationToken);
            await store.UpdateAsync(loaded, cancellationToken);
        }

        await using PigeonWatchDbContext readDb = CreateContext();
        ApplicationUser reloaded = (await CreateStore(readDb).FindByIdAsync(user.Id.ToString(), cancellationToken))!;

        Assert.True(reloaded.LockoutEnabled);
        Assert.Equal(lockoutEnd, reloaded.LockoutEnd);
        Assert.Equal(2, reloaded.AccessFailedCount);
    }

    public void Dispose()
    {
        connection.Dispose();
    }

    private async Task<IdentityResult> CreateAsync(ApplicationUser user)
    {
        await using PigeonWatchDbContext db = CreateContext();

        return await CreateStore(db).CreateAsync(user, cancellationToken);
    }

    private ApplicationUser NewUser(string email, string displayName)
    {
        return new()
        {
            Email = email,
            NormalizedEmail = normalizer.NormalizeEmail(email)!,
            UserName = email,
            NormalizedUserName = normalizer.NormalizeName(email)!,
            DisplayName = displayName,
            PasswordHash = "hash",
            SecurityStamp = Guid.NewGuid().ToString()
        };
    }

    private PigeonWatchUserStore CreateStore(PigeonWatchDbContext db)
    {
        return new(db, new UserAccountMapper(), normalizer, new IdentityErrorDescriber());
    }

    private PigeonWatchDbContext CreateContext()
    {
        DbContextOptions<PigeonWatchDbContext> options = new DbContextOptionsBuilder<PigeonWatchDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(new AuditSaveChangesInterceptor(
                new HttpContextCurrentUserProvider(new HttpContextAccessor()),
                TimeProvider.System))
            .Options;

        return new PigeonWatchDbContext(options);
    }
}

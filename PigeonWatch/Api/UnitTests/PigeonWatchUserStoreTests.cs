using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.Core;
using PigeonWatch.BusinessObjects;
using PigeonWatch.Data;
using PigeonWatch.Data.Auditing;
using PigeonWatch.Data.Identity;
using PigeonWatch.Data.Mappers;
using PigeonWatch.Data.Repositories;

namespace PigeonWatch.UnitTests;

public sealed class PigeonWatchUserStoreTests : IDisposable
{
    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly UpperInvariantLookupNormalizer normalizer = new();
    private readonly ILogger<PigeonWatchUserStore> storeLogger = Substitute.For<ILogger<PigeonWatchUserStore>>();
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
    public async Task Update_allows_a_case_only_rename_of_the_users_own_display_name()
    {
        ApplicationUser user = NewUser("user@example.com", "Pidgey");
        await CreateAsync(user);

        await using PigeonWatchDbContext db = CreateContext();
        PigeonWatchUserStore store = CreateStore(db);
        ApplicationUser loaded = (await store.FindByIdAsync(user.Id.ToString(), cancellationToken))!;
        loaded.DisplayName = "pIDGEY";
        IdentityResult result = await store.UpdateAsync(loaded, cancellationToken);

        Assert.True(result.Succeeded);
        await using PigeonWatchDbContext readDb = CreateContext();
        ApplicationUser reloaded = (await CreateStore(readDb).FindByIdAsync(user.Id.ToString(), cancellationToken))!;
        Assert.Equal("pIDGEY", reloaded.DisplayName);
    }

    [Fact]
    public async Task Rename_through_the_user_manager_accepts_the_unchanged_email_and_user_name()
    {
        ApplicationUser user = NewUser("user@example.com", "Pidgey");
        await CreateAsync(user);

        await using PigeonWatchDbContext db = CreateContext();
        AccountRepository repository = new(CreateUserManager(db), new UserAccountMapper(), NullLogger<AccountRepository>.Instance);
        ProfileUpdateResult result = await repository.UpdateDisplayNameAsync(user.Id, "Feathers", cancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(new UpdatedProfile("user@example.com", "Feathers"), result.Profile);
        await using PigeonWatchDbContext readDb = CreateContext();
        ApplicationUser reloaded = (await CreateStore(readDb).FindByIdAsync(user.Id.ToString(), cancellationToken))!;
        Assert.Equal("Feathers", reloaded.DisplayName);
        Assert.Equal("user@example.com", reloaded.Email);
        Assert.Equal("user@example.com", reloaded.UserName);
    }

    [Fact]
    public async Task Rename_through_the_user_manager_allows_a_case_only_change_and_rejects_a_name_taken_by_another_user()
    {
        ApplicationUser first = NewUser("first@example.com", "Pidgey");
        await CreateAsync(first);
        await CreateAsync(NewUser("second@example.com", "Feathers"));

        await using PigeonWatchDbContext db = CreateContext();
        AccountRepository repository = new(CreateUserManager(db), new UserAccountMapper(), NullLogger<AccountRepository>.Instance);
        ProfileUpdateResult caseOnly = await repository.UpdateDisplayNameAsync(first.Id, "pIDGEY", cancellationToken);
        ProfileUpdateResult taken = await repository.UpdateDisplayNameAsync(first.Id, "FEATHERS", cancellationToken);

        Assert.True(caseOnly.Succeeded);
        Assert.False(taken.Succeeded);
        Assert.Equal([AccountErrorCodes.DuplicateDisplayName], taken.Errors.Select(error => error.Code));
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
    public async Task Update_with_a_stale_concurrency_stamp_logs_a_warning_with_the_user_id()
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
        await firstStore.UpdateAsync(firstCopy, cancellationToken);
        storeLogger.ClearReceivedCalls();

        staleCopy.AccessFailedCount = 2;
        IdentityResult staleResult = await secondStore.UpdateAsync(staleCopy, cancellationToken);

        LogCall call = Assert.Single(LogCalls(storeLogger));
        Assert.Equal(LogLevel.Warning, call.Level);
        Assert.Equal(user.Id, call.State["UserId"]);
        Assert.Equal("update", call.State["Operation"]);
        Assert.Equal([new IdentityErrorDescriber().ConcurrencyFailure().Code], staleResult.Errors.Select(error => error.Code));
        AssertNoPersonalData(call);
    }

    [Fact]
    public async Task Update_and_delete_of_a_missing_user_log_a_warning_and_return_concurrency_failure()
    {
        ApplicationUser ghost = NewUser("ghost@example.com", "Ghost");
        ghost.Id = Guid.NewGuid();

        await using PigeonWatchDbContext db = CreateContext();
        PigeonWatchUserStore store = CreateStore(db);
        IdentityResult updateResult = await store.UpdateAsync(ghost, cancellationToken);
        IdentityResult deleteResult = await store.DeleteAsync(ghost, cancellationToken);

        List<LogCall> calls = LogCalls(storeLogger);
        Assert.Equal(["update", "delete"], calls.Select(call => call.State["Operation"]));
        Assert.All(calls, call =>
        {
            Assert.Equal(LogLevel.Warning, call.Level);
            Assert.Equal(ghost.Id, call.State["UserId"]);
            AssertNoPersonalData(call);
        });
        Assert.Equal([new IdentityErrorDescriber().ConcurrencyFailure().Code], updateResult.Errors.Select(error => error.Code));
        Assert.Equal([new IdentityErrorDescriber().ConcurrencyFailure().Code], deleteResult.Errors.Select(error => error.Code));
    }

    [Fact]
    public async Task Duplicate_display_name_branches_stay_silent()
    {
        await CreateAsync(NewUser("first@example.com", "Pidgey"));

        await CreateAsync(NewUser("second@example.com", "pIDGEY"));

        Assert.Empty(LogCalls(storeLogger));
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
        return new(db, new UserAccountMapper(), normalizer, new IdentityErrorDescriber(), storeLogger);
    }

    private UserManager<ApplicationUser> CreateUserManager(PigeonWatchDbContext db)
    {
        IdentityOptions options = new();
        options.User.RequireUniqueEmail = true;
        options.User.AllowedUserNameCharacters = string.Empty;
        IdentityErrorDescriber errorDescriber = new();

        return new UserManager<ApplicationUser>(
            CreateStore(db),
            Options.Create(options),
            new PasswordHasher<ApplicationUser>(),
            [new UserValidator<ApplicationUser>(errorDescriber)],
            [],
            normalizer,
            errorDescriber,
            null!,
            NullLogger<UserManager<ApplicationUser>>.Instance);
    }

    private static void AssertNoPersonalData(LogCall call)
    {
        Assert.DoesNotContain("example.com", call.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            call.State.Values,
            value => value?.ToString()?.Contains("example.com", StringComparison.OrdinalIgnoreCase) == true
                || value?.ToString()?.Contains("Pidgey", StringComparison.OrdinalIgnoreCase) == true
                || value?.ToString()?.Contains("Ghost", StringComparison.OrdinalIgnoreCase) == true);
    }

    private PigeonWatchDbContext CreateContext()
    {
        DbContextOptions<PigeonWatchDbContext> options = new DbContextOptionsBuilder<PigeonWatchDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(new AuditSaveChangesInterceptor(
                new HttpContextCurrentUserProvider(new HttpContextAccessor(), NullLogger<HttpContextCurrentUserProvider>.Instance),
                TimeProvider.System))
            .Options;

        return new PigeonWatchDbContext(options);
    }

    private static List<LogCall> LogCalls(ILogger logger)
    {
        List<LogCall> calls = logger.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(ILogger.Log))
            .Select(ToLogCall)
            .ToList();

        return calls;
    }

    private static LogCall ToLogCall(ICall call)
    {
        object?[] arguments = call.GetArguments();
        Dictionary<string, object?> state = [];

        foreach (KeyValuePair<string, object?> pair in (IEnumerable<KeyValuePair<string, object?>>)arguments[2]!)
            state[pair.Key] = pair.Value;

        string message = state["{OriginalFormat}"]?.ToString() ?? string.Empty;

        return new LogCall((LogLevel)arguments[0]!, state, message);
    }

    private sealed record LogCall(LogLevel Level, Dictionary<string, object?> State, string Message);
}

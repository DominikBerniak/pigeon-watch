using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.Core;
using PigeonWatch.Data;
using PigeonWatch.Data.Auditing;
using PigeonWatch.Data.Identity;
using PigeonWatch.Data.Mappers;

namespace PigeonWatch.UnitTests;

public sealed class PigeonWatchRoleStoreTests : IDisposable
{
    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly UpperInvariantLookupNormalizer normalizer = new();
    private readonly ILogger<PigeonWatchRoleStore> storeLogger = Substitute.For<ILogger<PigeonWatchRoleStore>>();
    private readonly CancellationToken cancellationToken = TestContext.Current.CancellationToken;

    public PigeonWatchRoleStoreTests()
    {
        connection.Open();
        using PigeonWatchDbContext db = CreateContext();
        db.Database.EnsureCreated();
    }

    [Fact]
    public async Task Create_assigns_the_generated_id_and_finds_the_role_by_normalized_name_and_id()
    {
        ApplicationRole role = new() { Name = "Admin", NormalizedName = "ADMIN" };

        IdentityResult result = await CreateAsync(role);

        Assert.True(result.Succeeded);
        Assert.NotEqual(Guid.Empty, role.Id);

        await using PigeonWatchDbContext db = CreateContext();
        PigeonWatchRoleStore store = CreateStore(db);
        ApplicationRole? byName = await store.FindByNameAsync("ADMIN", cancellationToken);
        ApplicationRole? byId = await store.FindByIdAsync(role.Id.ToString(), cancellationToken);

        Assert.Equal(role.Id, byName?.Id);
        Assert.Equal("Admin", byId?.Name);
        Assert.Null(await store.FindByNameAsync("Admin", cancellationToken));
        Assert.Null(await store.FindByIdAsync("not-a-guid", cancellationToken));
    }

    [Fact]
    public async Task Role_manager_creates_a_role_once_and_rejects_the_same_name_in_another_case()
    {
        await using PigeonWatchDbContext db = CreateContext();
        RoleManager<ApplicationRole> roleManager = CreateRoleManager(db);

        IdentityResult first = await roleManager.CreateAsync(new ApplicationRole { Name = "Admin" });
        IdentityResult duplicate = await roleManager.CreateAsync(new ApplicationRole { Name = "admin" });

        Assert.True(first.Succeeded);
        Assert.True(await roleManager.RoleExistsAsync("ADMIN"));
        Assert.Equal([new IdentityErrorDescriber().DuplicateRoleName("admin").Code], duplicate.Errors.Select(error => error.Code));
    }

    [Fact]
    public async Task Update_with_a_stale_concurrency_stamp_returns_concurrency_failure_and_logs_a_warning()
    {
        ApplicationRole role = new() { Name = "Admin", NormalizedName = "ADMIN" };
        await CreateAsync(role);

        await using PigeonWatchDbContext firstDb = CreateContext();
        await using PigeonWatchDbContext secondDb = CreateContext();
        PigeonWatchRoleStore firstStore = CreateStore(firstDb);
        PigeonWatchRoleStore secondStore = CreateStore(secondDb);
        ApplicationRole firstCopy = (await firstStore.FindByIdAsync(role.Id.ToString(), cancellationToken))!;
        ApplicationRole staleCopy = (await secondStore.FindByIdAsync(role.Id.ToString(), cancellationToken))!;

        firstCopy.Name = "Administrator";
        IdentityResult firstResult = await firstStore.UpdateAsync(firstCopy, cancellationToken);
        storeLogger.ClearReceivedCalls();
        staleCopy.Name = "Moderator";
        IdentityResult staleResult = await secondStore.UpdateAsync(staleCopy, cancellationToken);

        Assert.True(firstResult.Succeeded);
        Assert.Equal([new IdentityErrorDescriber().ConcurrencyFailure().Code], staleResult.Errors.Select(error => error.Code));
        (LogLevel Level, Exception? Exception, Dictionary<string, object?> State) call = Assert.Single(LogCalls(storeLogger));
        Assert.Equal(LogLevel.Warning, call.Level);
        Assert.IsType<DbUpdateConcurrencyException>(call.Exception);
        Assert.Equal(role.Id, call.State["RoleId"]);
        Assert.Equal("update", call.State["Operation"]);
    }

    [Fact]
    public async Task Update_and_delete_of_a_missing_role_return_concurrency_failure()
    {
        ApplicationRole ghost = new() { Id = Guid.NewGuid(), Name = "Ghost", NormalizedName = "GHOST" };

        await using PigeonWatchDbContext db = CreateContext();
        PigeonWatchRoleStore store = CreateStore(db);
        IdentityResult updateResult = await store.UpdateAsync(ghost, cancellationToken);
        IdentityResult deleteResult = await store.DeleteAsync(ghost, cancellationToken);

        Assert.Equal([new IdentityErrorDescriber().ConcurrencyFailure().Code], updateResult.Errors.Select(error => error.Code));
        Assert.Equal([new IdentityErrorDescriber().ConcurrencyFailure().Code], deleteResult.Errors.Select(error => error.Code));
        Assert.Equal(["update", "delete"], LogCalls(storeLogger).Select(call => call.State["Operation"]));
    }

    [Fact]
    public async Task Create_sets_the_audit_columns_without_the_store_assigning_them()
    {
        await CreateAsync(new ApplicationRole { Name = "Admin", NormalizedName = "ADMIN" });

        await using PigeonWatchDbContext db = CreateContext();
        string createUser = await db.Database
            .SqlQueryRaw<string>("SELECT CREATE_USER AS Value FROM ROLE")
            .SingleAsync(cancellationToken);

        Assert.Equal(HttpContextCurrentUserProvider.SystemUserName, createUser);
    }

    public void Dispose()
    {
        connection.Dispose();
    }

    private async Task<IdentityResult> CreateAsync(ApplicationRole role)
    {
        await using PigeonWatchDbContext db = CreateContext();

        return await CreateStore(db).CreateAsync(role, cancellationToken);
    }

    private PigeonWatchRoleStore CreateStore(PigeonWatchDbContext db)
    {
        return new(db, new RoleMapper(), new IdentityErrorDescriber(), storeLogger);
    }

    private RoleManager<ApplicationRole> CreateRoleManager(PigeonWatchDbContext db)
    {
        return new RoleManager<ApplicationRole>(
            CreateStore(db),
            [new RoleValidator<ApplicationRole>()],
            normalizer,
            new IdentityErrorDescriber(),
            NullLogger<RoleManager<ApplicationRole>>.Instance);
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

    private static List<(LogLevel Level, Exception? Exception, Dictionary<string, object?> State)> LogCalls(ILogger logger)
    {
        return logger.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(ILogger.Log))
            .Select(ToLogCall)
            .ToList();
    }

    private static (LogLevel Level, Exception? Exception, Dictionary<string, object?> State) ToLogCall(ICall call)
    {
        object?[] arguments = call.GetArguments();
        Dictionary<string, object?> state = [];

        foreach (KeyValuePair<string, object?> pair in (IEnumerable<KeyValuePair<string, object?>>)arguments[2]!)
            state[pair.Key] = pair.Value;

        return ((LogLevel)arguments[0]!, (Exception?)arguments[3], state);
    }
}

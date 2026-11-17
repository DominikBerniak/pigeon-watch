using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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

namespace PigeonWatch.UnitTests;

public sealed class AdminRoleBootstrapperTests : IDisposable
{
    private const string adminEmail = "admin@example.com";
    private const string memberEmail = "member@example.com";
    private const string ghostEmail = "ghost@example.com";

    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly ILogger<AdminRoleBootstrapper> bootstrapperLogger = Substitute.For<ILogger<AdminRoleBootstrapper>>();
    private readonly ILogger<AdminRoleBootstrapWorker> workerLogger = Substitute.For<ILogger<AdminRoleBootstrapWorker>>();
    private readonly CancellationToken cancellationToken = TestContext.Current.CancellationToken;
    private readonly ServiceProvider provider;

    public AdminRoleBootstrapperTests()
    {
        connection.Open();
        provider = BuildProvider(connection);

        using IServiceScope scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<PigeonWatchDbContext>().Database.EnsureCreated();
    }

    [Fact]
    public async Task Creates_the_admin_role_when_it_is_missing()
    {
        await CreateBootstrapper([]).RunAsync(cancellationToken);

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        RoleManager<ApplicationRole> roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();

        Assert.True(await roleManager.RoleExistsAsync(RoleNames.Admin));
    }

    [Fact]
    public async Task Grants_the_admin_role_to_a_listed_registered_email()
    {
        await RegisterAsync(adminEmail, "Admin");

        await CreateBootstrapper([adminEmail]).RunAsync(cancellationToken);

        Assert.True(await IsAdminAsync(adminEmail));
    }

    [Fact]
    public async Task A_second_run_is_a_no_op()
    {
        await RegisterAsync(adminEmail, "Admin");
        AdminRoleBootstrapper bootstrapper = CreateBootstrapper([adminEmail]);
        await bootstrapper.RunAsync(cancellationToken);
        bootstrapperLogger.ClearReceivedCalls();

        await bootstrapper.RunAsync(cancellationToken);

        Assert.True(await IsAdminAsync(adminEmail));
        Assert.Equal(1, await CountAsync("ROLE"));
        Assert.Equal(1, await CountAsync("USER_ROLE"));
        Assert.Empty(LogCalls(bootstrapperLogger));
    }

    [Fact]
    public async Task An_unlisted_user_is_untouched()
    {
        await RegisterAsync(adminEmail, "Admin");
        await RegisterAsync(memberEmail, "Member");

        await CreateBootstrapper([adminEmail]).RunAsync(cancellationToken);

        Assert.True(await IsAdminAsync(adminEmail));
        Assert.False(await IsAdminAsync(memberEmail));
    }

    [Fact]
    public async Task A_user_removed_from_the_list_keeps_the_role()
    {
        await RegisterAsync(adminEmail, "Admin");
        await CreateBootstrapper([adminEmail]).RunAsync(cancellationToken);

        await CreateBootstrapper([]).RunAsync(cancellationToken);

        Assert.True(await IsAdminAsync(adminEmail));
    }

    [Fact]
    public async Task An_unregistered_email_is_logged_at_warning_without_the_email_and_the_next_email_is_still_granted()
    {
        await RegisterAsync(adminEmail, "Admin");

        Exception? failure = await Record.ExceptionAsync(() => CreateBootstrapper([ghostEmail, adminEmail]).RunAsync(cancellationToken));

        Assert.Null(failure);
        Assert.True(await IsAdminAsync(adminEmail));
        (LogLevel Level, Exception? Exception, Dictionary<string, object?> State) warning =
            Assert.Single(LogCalls(bootstrapperLogger), call => call.Level >= LogLevel.Warning);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Equal(0, warning.State["AdminEmailIndex"]);
        Assert.DoesNotContain(
            warning.State.Values,
            value => value?.ToString()?.Contains("example.com", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public async Task The_worker_logs_a_failed_bootstrap_at_error_and_completes()
    {
        await using SqliteConnection missingSchema = new("DataSource=:memory:");
        await using ServiceProvider brokenProvider = BuildProvider(missingSchema);
        AdminRoleBootstrapper bootstrapper = new(
            brokenProvider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new ModerationOptions { AdminEmails = [adminEmail] }),
            bootstrapperLogger);
        using AdminRoleBootstrapWorker worker = new(bootstrapper, workerLogger);

        await worker.StartAsync(cancellationToken);
        await worker.ExecuteTask!;

        (LogLevel Level, Exception? Exception, Dictionary<string, object?> State) error = Assert.Single(LogCalls(workerLogger));
        Assert.Equal(LogLevel.Error, error.Level);
        Assert.NotNull(error.Exception);
        Assert.True(worker.ExecuteTask.IsCompletedSuccessfully);
    }

    public void Dispose()
    {
        provider.Dispose();
        connection.Dispose();
    }

    private AdminRoleBootstrapper CreateBootstrapper(List<string> adminEmails)
    {
        return new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new ModerationOptions { AdminEmails = adminEmails }),
            bootstrapperLogger);
    }

    private async Task RegisterAsync(string email, string displayName)
    {
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        UserManager<ApplicationUser> userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        IdentityResult result = await userManager.CreateAsync(new ApplicationUser { Email = email, UserName = email, DisplayName = displayName });

        Assert.True(result.Succeeded);
    }

    private async Task<bool> IsAdminAsync(string email)
    {
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        UserManager<ApplicationUser> userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        ApplicationUser user = (await userManager.FindByEmailAsync(email))!;

        return await userManager.IsInRoleAsync(user, RoleNames.Admin);
    }

    private async Task<int> CountAsync(string tableName)
    {
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        PigeonWatchDbContext db = scope.ServiceProvider.GetRequiredService<PigeonWatchDbContext>();
        int count = await db.Database
            .SqlQueryRaw<int>($"SELECT COUNT(*) AS Value FROM {tableName}")
            .SingleAsync(cancellationToken);

        return count;
    }

    private static ServiceProvider BuildProvider(SqliteConnection databaseConnection)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddDbContext<PigeonWatchDbContext>(options => options
            .UseSqlite(databaseConnection)
            .AddInterceptors(new AuditSaveChangesInterceptor(
                new HttpContextCurrentUserProvider(new HttpContextAccessor(), NullLogger<HttpContextCurrentUserProvider>.Instance),
                TimeProvider.System)));
        services.AddScoped<IUserAccountMapper, UserAccountMapper>();
        services.AddScoped<IRoleMapper, RoleMapper>();
        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.User.AllowedUserNameCharacters = string.Empty;
            })
            .AddRoles<ApplicationRole>()
            .AddUserStore<PigeonWatchUserStore>()
            .AddRoleStore<PigeonWatchRoleStore>();

        return services.BuildServiceProvider();
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

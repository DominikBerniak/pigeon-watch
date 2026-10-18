using System.Net;
using System.Net.Http.Json;

namespace PigeonWatch.IntegrationTests;

public sealed class PrivacySweepTests : IDisposable
{
    private const string aliceEmail = "Alice.Tester+pw@Example.com";
    private const string aliceDisplayName = "AliceTester";
    private const string bobEmail = "bob.tester@example.net";
    private const string bobDisplayName = "BobTester";
    private const string password = "Passw0rd!sweep1";
    private const string newPassword = "Passw0rd!sweep2";
    private const string systemUser = "SYSTEM";

    private readonly ManualTimeProvider clock = new(DateTimeOffset.UtcNow);
    private readonly PigeonWatchApiFactory factory;
    private readonly EndpointFixtureRegistry registry = new();
    private readonly ResponseLeakScanner scanner = new();
    private readonly List<TestUser> users = [];
    private readonly CancellationToken cancellationToken = TestContext.Current.CancellationToken;

    public PrivacySweepTests()
    {
        factory = new PigeonWatchApiFactory(clock);
    }

    [Fact]
    public async Task Scanner_finds_the_own_email_in_the_own_general_configuration_response()
    {
        TestUser alice = await SeedUserAsync(aliceEmail, aliceDisplayName);
        TestUser bob = await SeedUserAsync(bobEmail, bobDisplayName);
        using HttpRequestMessage request = new(HttpMethod.Get, "/configuration/general");

        using HttpResponseMessage response = await factory.SendFromFreshAddressAsync(request, alice.AccessToken, cancellationToken);
        IReadOnlyList<LeakHit> ownHits = await scanner.ScanAsync(response, ResponseLeakScanner.NeedlesFor(alice.Email), cancellationToken);
        IReadOnlyList<LeakHit> foreignHits = await scanner.ScanAsync(response, ResponseLeakScanner.NeedlesFor(bob.Email), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(ownHits, hit => hit.Location.StartsWith("json string value", StringComparison.Ordinal));
        Assert.Empty(foreignHits);
    }

    [Fact]
    public async Task Sweep_as_anonymous_finds_no_registered_email_in_any_response()
    {
        SweepWorld world = await SeedWorldAsync();

        IReadOnlyList<string> failures = await CreateSweep().RunAsync(
            "anonymous",
            null,
            [aliceEmail, bobEmail],
            world.Context,
            cancellationToken);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public async Task Sweep_as_bob_finds_no_other_users_email_in_any_response()
    {
        SweepWorld world = await SeedWorldAsync();

        IReadOnlyList<string> failures = await CreateSweep().RunAsync(
            "bob",
            world.Bob.AccessToken,
            [aliceEmail],
            world.Context,
            cancellationToken);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public async Task Sweep_as_alice_finds_no_other_users_email_in_any_response()
    {
        SweepWorld world = await SeedWorldAsync();
        clock.Advance(TimeSpan.FromMinutes(2));

        IReadOnlyList<string> failures = await CreateSweep().RunAsync(
            "alice",
            world.Alice.AccessToken,
            [bobEmail],
            world.Context,
            cancellationToken);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public async Task Sweep_as_bob_with_a_closed_database_finds_no_email_in_the_server_error_response()
    {
        SweepWorld world = await SeedWorldAsync();
        List<string> needles = new[] { aliceEmail, bobEmail }.SelectMany(email => ResponseLeakScanner.NeedlesFor(email)).ToList();
        factory.CloseDatabase();
        using HttpRequestMessage request = new(HttpMethod.Get, "/sightings/mine");

        using HttpResponseMessage response = await factory.SendFromFreshAddressAsync(request, world.Bob.AccessToken, cancellationToken);
        IReadOnlyList<LeakHit> hits = await scanner.ScanAsync(response, needles, cancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.True(
            hits.Count == 0,
            string.Join(Environment.NewLine, hits.Select(hit => $"server error response leaked an email in {hit.Location}")));
    }

    [Fact]
    public async Task Stubbed_identity_routes_answer_not_found_with_no_body_and_manage_routes_ask_anonymous_callers_to_sign_in()
    {
        SweepWorld world = await SeedWorldAsync();
        HashSet<string> routed = new EndpointCatalog(factory.Services).GetEndpoints().Select(endpoint => endpoint.Key).ToHashSet();
        List<string> problems = [];

        foreach (string key in EndpointFixtureRegistry.NotFoundStubKeys)
        {
            await CheckStubAsync(key, null, HttpStatusCode.NotFound, world.Context, routed, problems);
            await CheckStubAsync(key, world.Bob.AccessToken, HttpStatusCode.NotFound, world.Context, routed, problems);
        }

        foreach (string key in EndpointFixtureRegistry.ManageStubKeys)
        {
            await CheckStubAsync(key, null, HttpStatusCode.Unauthorized, world.Context, routed, problems);
            await CheckStubAsync(key, world.Bob.AccessToken, HttpStatusCode.NotFound, world.Context, routed, problems);
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public async Task Audit_columns_hold_a_user_id_or_system_and_never_an_email()
    {
        TestUser alice = await SeedUserAsync(aliceEmail, aliceDisplayName);
        TestUser bob = await SeedUserAsync(bobEmail, bobDisplayName);
        await alice.CreateSightingAsync(cancellationToken: cancellationToken);
        await bob.CreateSightingAsync(cancellationToken: cancellationToken);

        using HttpResponseMessage rename = await alice.Client.PutAsJsonAsync(
            "/account/profile",
            new { displayName = "AliceRenamed" },
            cancellationToken);
        using HttpRequestMessage changePassword = new(HttpMethod.Put, "/account/password")
        {
            Content = JsonContent.Create(new { currentPassword = password, newPassword })
        };
        using HttpResponseMessage passwordChange = await factory.SendFromFreshAddressAsync(changePassword, alice.AccessToken, cancellationToken);
        using HttpClient anonymousClient = factory.CreateApiClient();
        using HttpResponseMessage failedLogin = await new AccountApiClient(anonymousClient).LoginAsync(bobEmail, "Wr0ngPassw0rd!", cancellationToken);
        List<string> needles = new[] { aliceEmail, bobEmail }.SelectMany(email => ResponseLeakScanner.NeedlesFor(email)).ToList();
        List<string> offenders = [];

        offenders.AddRange(FindAuditOffenders("USER_ACCOUNT", factory.ReadUserAccountAuditUsers(), needles));
        offenders.AddRange(FindAuditOffenders("SIGHTING", factory.ReadSightingAuditUsers(), needles));

        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);
        Assert.Equal(HttpStatusCode.OK, passwordChange.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, failedLogin.StatusCode);
        Assert.Equal(2, factory.ReadUserAccountAuditUsers().Count);
        Assert.Equal(2, factory.ReadSightingAuditUsers().Count);
        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    public void Dispose()
    {
        foreach (TestUser user in users)
            user.Dispose();

        factory.Dispose();
    }

    private static List<string> FindAuditOffenders(string table, IReadOnlyList<AuditUsers> rows, IReadOnlyList<string> needles)
    {
        List<string> offenders = [];

        foreach (AuditUsers row in rows)
        {
            foreach (string value in new[] { row.CreateUser, row.UpdateUser })
            {
                bool isUserIdOrSystem = value == systemUser || Guid.TryParse(value, out Guid _);
                bool holdsNeedle = needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));

                if (!isUserIdOrSystem || holdsNeedle)
                    offenders.Add($"{table} audit column holds a value that is neither a user id nor {systemUser}");
            }
        }

        return offenders;
    }

    private async Task CheckStubAsync(
        string key,
        string? accessToken,
        HttpStatusCode expected,
        FixtureContext context,
        HashSet<string> routed,
        List<string> problems)
    {
        string caller = accessToken is null ? "anonymous" : "signed in";

        if (!routed.Contains(key))
        {
            problems.Add($"{key} is not routed any more");

            return;
        }

        foreach (FixtureRequest request in registry.BuildRequests(key, context))
        {
            using HttpResponseMessage response = await factory.SendFromFreshAddressAsync(request.Message, accessToken, cancellationToken);
            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            request.Message.Dispose();

            if (response.StatusCode != expected)
                problems.Add($"{caller}: {key} answered {(int)response.StatusCode}, expected {(int)expected}");

            if (body.Length != 0)
                problems.Add($"{caller}: {key} answered a body");
        }
    }

    private PrivacySweep CreateSweep()
    {
        return new PrivacySweep(factory, registry, scanner);
    }

    private async Task<TestUser> SeedUserAsync(string email, string displayName)
    {
        TestUser user = await factory.CreateUserAsync(email, password, displayName, cancellationToken);
        users.Add(user);

        return user;
    }

    private async Task<SweepWorld> SeedWorldAsync()
    {
        TestUser alice = await SeedUserAsync(aliceEmail, aliceDisplayName);
        TestUser bob = await SeedUserAsync(bobEmail, bobDisplayName);
        Guid sightingId = await alice.CreateSightingAsync(cancellationToken: cancellationToken);
        FixtureContext context = new([alice.Email, bob.Email], [alice.DisplayName, bob.DisplayName], sightingId);

        return new SweepWorld(alice, bob, context);
    }

    private sealed record SweepWorld(TestUser Alice, TestUser Bob, FixtureContext Context);
}

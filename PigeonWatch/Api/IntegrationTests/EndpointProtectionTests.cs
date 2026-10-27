using System.Net;

namespace PigeonWatch.IntegrationTests;

public sealed class EndpointProtectionTests : IDisposable
{
    private const string aliceEmail = "alice.protect+x@example.com";
    private const string aliceDisplayName = "AliceProtect";
    private const string bobEmail = "bob.protect@example.net";
    private const string bobDisplayName = "BobProtect";
    private const string password = "Passw0rd!protect1";

    private static readonly IReadOnlyList<EndpointExemption> anonymousEndpoints =
    [
        new("POST account/register", "Registration is open to people who have no account yet and is guarded by the rate limiter."),
        new("POST auth/login", "Sign-in is open to people who are not signed in yet and is guarded by the rate limiter and lockout."),
        new("POST auth/refresh", "Refreshing presents a refresh token instead of a bearer header, so the endpoint is anonymous by design."),
        new("GET configuration/client", "Validation rules are public configuration that the sign-up screen needs before sign-in."),
        new("GET resources/{culture}", "UI labels are public text that every screen needs before sign-in."),
        new("POST client-errors", "Browser error reports arrive from visitors who may not be signed in, so the endpoint is anonymous by design and is guarded by the rate limiter, a body cap and allowlist validation."),
        new("POST auth/register", "Stubbed Identity route that answers 404 for every caller, so it is stubbed rather than protected."),
        new("GET auth/confirmEmail", "Stubbed Identity route that answers 404 for every caller, so it is stubbed rather than protected."),
        new("POST auth/resendConfirmationEmail", "Stubbed Identity route that answers 404 for every caller, so it is stubbed rather than protected."),
        new("POST auth/forgotPassword", "Stubbed Identity route that answers 404 for every caller, so it is stubbed rather than protected."),
        new("POST auth/resetPassword", "Stubbed Identity route that answers 404 for every caller, so it is stubbed rather than protected.")
    ];

    private readonly PigeonWatchApiFactory factory = new();
    private readonly EndpointFixtureRegistry registry = new();
    private readonly ResponseLeakScanner scanner = new();
    private readonly List<TestUser> users = [];
    private readonly CancellationToken cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public void Anonymous_list_covers_every_endpoint_without_authorization_metadata_and_has_no_stale_entries()
    {
        IReadOnlyList<RoutedEndpoint> endpoints = new EndpointCatalog(factory.Services).GetEndpoints();
        HashSet<string> listed = anonymousEndpoints.Select(entry => entry.Key).ToHashSet();
        HashSet<string> unprotected = endpoints
            .Where(endpoint => !endpoint.RequiresAuthorization)
            .Select(endpoint => endpoint.Key)
            .ToHashSet();
        HashSet<string> protectedKeys = endpoints
            .Where(endpoint => endpoint.RequiresAuthorization)
            .Select(endpoint => endpoint.Key)
            .ToHashSet();
        List<string> missing = unprotected.Where(key => !listed.Contains(key)).ToList();
        List<string> stale = listed.Where(key => !unprotected.Contains(key) && !protectedKeys.Contains(key)).ToList();
        List<string> nowProtected = listed.Where(protectedKeys.Contains).ToList();
        List<string> duplicated = anonymousEndpoints
            .GroupBy(entry => entry.Key)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        Assert.True(endpoints.Count > 0, "The endpoint catalog is empty, so the guard would be vacuous.");
        Assert.True(anonymousEndpoints.Count > 0, "The anonymous list is empty, so the guard would be vacuous.");
        Assert.True(
            missing.Count == 0,
            $"Endpoints without authorization metadata that are not on the anonymous list:{Environment.NewLine}{string.Join(Environment.NewLine, missing)}");
        Assert.True(
            stale.Count == 0,
            $"Anonymous list entries that match no routed endpoint:{Environment.NewLine}{string.Join(Environment.NewLine, stale)}");
        Assert.True(
            nowProtected.Count == 0,
            $"Anonymous list entries that now carry authorization metadata:{Environment.NewLine}{string.Join(Environment.NewLine, nowProtected)}");
        Assert.True(
            duplicated.Count == 0,
            $"Anonymous list entries declared more than once:{Environment.NewLine}{string.Join(Environment.NewLine, duplicated)}");
        Assert.All(anonymousEndpoints, entry => Assert.False(string.IsNullOrWhiteSpace(entry.Reason)));
    }

    [Fact]
    public async Task Anonymous_calls_to_every_protected_endpoint_get_unauthorized_without_a_leaked_email()
    {
        TestUser alice = await SeedUserAsync(aliceEmail, aliceDisplayName);
        TestUser bob = await SeedUserAsync(bobEmail, bobDisplayName);
        Guid sightingId = await alice.CreateSightingAsync(cancellationToken: cancellationToken);
        FixtureContext context = new([alice.Email, bob.Email], [alice.DisplayName, bob.DisplayName], sightingId);
        List<string> needles = new[] { aliceEmail, bobEmail }.SelectMany(email => ResponseLeakScanner.NeedlesFor(email)).ToList();
        List<string> protectedKeys = new EndpointCatalog(factory.Services).GetEndpoints()
            .Where(endpoint => endpoint.RequiresAuthorization)
            .Select(endpoint => endpoint.Key)
            .ToList();
        HashSet<string> requestedKeys = [];
        List<string> failures = [];

        foreach (string key in protectedKeys)
        {
            foreach (FixtureRequest request in registry.BuildRequests(key, context))
            {
                requestedKeys.Add(request.Key);

                using HttpResponseMessage response = await factory.SendFromFreshAddressAsync(request.Message, null, cancellationToken);
                IReadOnlyList<LeakHit> hits = await scanner.ScanAsync(response, needles, cancellationToken);
                string label = $"{request.Key} ({request.Description})";

                if (response.StatusCode != HttpStatusCode.Unauthorized)
                    failures.Add($"{label} answered {(int)response.StatusCode} to an anonymous caller, expected 401");

                foreach (LeakHit hit in hits)
                    failures.Add($"{label} leaked an email in {hit.Location}");

                request.Message.Dispose();
            }
        }

        foreach (string key in protectedKeys)
        {
            if (!requestedKeys.Contains(key))
                failures.Add($"No anonymous request was sent to {key}");
        }

        Assert.True(protectedKeys.Count > 0, "No endpoint carries authorization metadata, so the check would be vacuous.");
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    public void Dispose()
    {
        foreach (TestUser user in users)
            user.Dispose();

        factory.Dispose();
    }

    private async Task<TestUser> SeedUserAsync(string email, string displayName)
    {
        TestUser user = await factory.CreateUserAsync(email, password, displayName, cancellationToken);
        users.Add(user);

        return user;
    }
}

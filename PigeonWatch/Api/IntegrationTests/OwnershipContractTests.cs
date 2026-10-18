using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace PigeonWatch.IntegrationTests;

public sealed class OwnershipContractTests : IDisposable
{
    private const string aliceEmail = "alice.owner+x@example.com";
    private const string aliceDisplayName = "AliceOwner";
    private const string alicePassword = "Passw0rd!alice1";
    private const string bobEmail = "bob.owner@example.net";
    private const string bobDisplayName = "BobOwner";
    private const string bobPassword = "Passw0rd!bobby1";
    private const string bobNewPassword = "Passw0rd!bobby2";
    private const string stolenPassword = "Passw0rd!stolen1";
    private const string nearbyUri = "/sightings/nearby?latitude=52.2297&longitude=21.0122";

    private readonly PigeonWatchApiFactory factory = new();
    private readonly ResponseLeakScanner scanner = new();
    private readonly List<TestUser> users = [];
    private readonly CancellationToken cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Mine_lists_only_the_callers_own_sightings_for_alice_and_for_bob()
    {
        TestUser alice = await SeedUserAsync(aliceEmail, alicePassword, aliceDisplayName);
        TestUser bob = await SeedUserAsync(bobEmail, bobPassword, bobDisplayName);
        Guid aliceSightingId = await alice.CreateSightingAsync(cancellationToken: cancellationToken);
        Guid bobSightingId = await bob.CreateSightingAsync(cancellationToken: cancellationToken);

        List<SightingSummary> aliceMine = await ReadMineAsync(alice);
        List<SightingSummary> bobMine = await ReadMineAsync(bob);

        Assert.Equal([new SightingSummary(aliceSightingId, aliceDisplayName)], aliceMine);
        Assert.Equal([new SightingSummary(bobSightingId, bobDisplayName)], bobMine);
    }

    [Fact]
    public async Task Account_changes_by_bob_rename_leave_alices_display_name_and_email_unchanged()
    {
        TestUser alice = await SeedUserAsync(aliceEmail, alicePassword, aliceDisplayName);
        TestUser bob = await SeedUserAsync(bobEmail, bobPassword, bobDisplayName);

        using HttpResponseMessage rename = await bob.Client.PutAsJsonAsync(
            "/account/profile",
            new { displayName = "BobRenamed", userId = alice.Id, id = alice.Id, email = alice.Email },
            cancellationToken);
        string renameBody = await rename.Content.ReadAsStringAsync(cancellationToken);
        using JsonDocument renamed = JsonDocument.Parse(renameBody);
        AccountView bobAccount = await ReadAccountAsync(bob);
        AccountView aliceAccount = await ReadAccountAsync(alice);

        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);
        Assert.Equal("BobRenamed", renamed.RootElement.GetProperty("displayName").GetString());
        Assert.Equal(bobEmail, renamed.RootElement.GetProperty("email").GetString(), ignoreCase: true);
        Assert.Equal(new AccountView(bobEmail, "BobRenamed"), bobAccount);
        Assert.Equal(new AccountView(aliceEmail, aliceDisplayName), aliceAccount);
    }

    [Fact]
    public async Task Account_changes_by_bob_rename_to_alices_display_name_is_refused_and_alice_is_unchanged()
    {
        TestUser alice = await SeedUserAsync(aliceEmail, alicePassword, aliceDisplayName);
        TestUser bob = await SeedUserAsync(bobEmail, bobPassword, bobDisplayName);

        using HttpResponseMessage rename = await bob.Client.PutAsJsonAsync(
            "/account/profile",
            new { displayName = aliceDisplayName },
            cancellationToken);
        string[] errorCodes = await new AccountApiClient(bob.Client).ReadErrorCodesAsync(rename, cancellationToken);
        AccountView bobAccount = await ReadAccountAsync(bob);
        AccountView aliceAccount = await ReadAccountAsync(alice);

        Assert.Equal(HttpStatusCode.BadRequest, rename.StatusCode);
        Assert.Equal(["DuplicateDisplayName"], errorCodes);
        Assert.Equal(new AccountView(bobEmail, bobDisplayName), bobAccount);
        Assert.Equal(new AccountView(aliceEmail, aliceDisplayName), aliceAccount);
    }

    [Fact]
    public async Task Account_changes_by_bob_password_change_leave_alices_password_unchanged()
    {
        TestUser alice = await SeedUserAsync(aliceEmail, alicePassword, aliceDisplayName);
        TestUser bob = await SeedUserAsync(bobEmail, bobPassword, bobDisplayName);

        using HttpResponseMessage change = await bob.Client.PutAsJsonAsync(
            "/account/password",
            new { currentPassword = bobPassword, newPassword = bobNewPassword, userId = alice.Id, email = alice.Email },
            cancellationToken);
        HttpStatusCode aliceLogin = await LoginAsync(aliceEmail, alicePassword);
        HttpStatusCode bobOldLogin = await LoginAsync(bobEmail, bobPassword);
        HttpStatusCode bobNewLogin = await LoginAsync(bobEmail, bobNewPassword);
        AccountView aliceAccount = await ReadAccountAsync(alice);

        Assert.Equal(HttpStatusCode.OK, change.StatusCode);
        Assert.Equal(HttpStatusCode.OK, aliceLogin);
        Assert.Equal(HttpStatusCode.Unauthorized, bobOldLogin);
        Assert.Equal(HttpStatusCode.OK, bobNewLogin);
        Assert.Equal(new AccountView(aliceEmail, aliceDisplayName), aliceAccount);
    }

    [Fact]
    public async Task Password_change_with_another_users_current_password_is_refused_as_a_mismatch()
    {
        TestUser alice = await SeedUserAsync(aliceEmail, alicePassword, aliceDisplayName);
        TestUser bob = await SeedUserAsync(bobEmail, bobPassword, bobDisplayName);

        using HttpResponseMessage change = await bob.Client.PutAsJsonAsync(
            "/account/password",
            new { currentPassword = alicePassword, newPassword = stolenPassword, userId = alice.Id },
            cancellationToken);
        string[] errorCodes = await new AccountApiClient(bob.Client).ReadErrorCodesAsync(change, cancellationToken);
        HttpStatusCode aliceLogin = await LoginAsync(aliceEmail, alicePassword);
        HttpStatusCode bobLogin = await LoginAsync(bobEmail, bobPassword);
        HttpStatusCode aliceStolenLogin = await LoginAsync(aliceEmail, stolenPassword);
        HttpStatusCode bobStolenLogin = await LoginAsync(bobEmail, stolenPassword);

        Assert.Equal(HttpStatusCode.BadRequest, change.StatusCode);
        Assert.Equal(["PasswordMismatch"], errorCodes);
        Assert.Equal(HttpStatusCode.OK, aliceLogin);
        Assert.Equal(HttpStatusCode.OK, bobLogin);
        Assert.Equal(HttpStatusCode.Unauthorized, aliceStolenLogin);
        Assert.Equal(HttpStatusCode.Unauthorized, bobStolenLogin);
    }

    [Fact]
    public async Task Sighting_is_attributed_to_bob_when_the_body_carries_forged_ownership_fields()
    {
        TestUser alice = await SeedUserAsync(aliceEmail, alicePassword, aliceDisplayName);
        TestUser bob = await SeedUserAsync(bobEmail, bobPassword, bobDisplayName);
        Guid aliceSightingId = await alice.CreateSightingAsync(cancellationToken: cancellationToken);

        using HttpResponseMessage create = await bob.Client.PostAsJsonAsync(
            "/sightings",
            new
            {
                name = "Forged pigeon",
                description = "A pigeon with a bent wing near the fountain.",
                behaviour = "Hops instead of flying.",
                latitude = 52.2297,
                longitude = 21.0122,
                status = 1,
                criticality = 2,
                id = aliceSightingId,
                reporterId = alice.Id,
                reporterDisplayName = aliceDisplayName,
                reporterEmail = aliceEmail,
                email = aliceEmail,
                userId = alice.Id,
                createUser = alice.Id.ToString(),
                reporter = new { id = alice.Id, displayName = aliceDisplayName, email = aliceEmail }
            },
            cancellationToken);
        IReadOnlyList<LeakHit> leaks = await scanner.ScanAsync(create, ResponseLeakScanner.NeedlesFor(aliceEmail), cancellationToken);
        string createBody = await create.Content.ReadAsStringAsync(cancellationToken);
        using JsonDocument created = JsonDocument.Parse(createBody);
        Guid createdId = created.RootElement.GetProperty("id").GetGuid();
        string createdReporter = created.RootElement.GetProperty("reporterDisplayName").GetString()!;
        List<SightingSummary> bobMine = await ReadMineAsync(bob);
        List<SightingSummary> aliceMine = await ReadMineAsync(alice);
        List<Guid> createUsers = factory.ReadSightingAuditUsers().Select(row => Guid.Parse(row.CreateUser)).ToList();

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.Empty(leaks);
        Assert.NotEqual(aliceSightingId, createdId);
        Assert.Equal(bobDisplayName, createdReporter);
        Assert.Equal([new SightingSummary(createdId, bobDisplayName)], bobMine);
        Assert.Equal([new SightingSummary(aliceSightingId, aliceDisplayName)], aliceMine);
        Assert.Equal(2, createUsers.Count);
        Assert.Single(createUsers, id => id == bob.Id);
        Assert.Single(createUsers, id => id == alice.Id);
    }

    [Fact]
    public async Task Open_reads_by_design_show_alices_sighting_to_bob_by_id_and_nearby_with_a_display_name_and_no_email()
    {
        TestUser alice = await SeedUserAsync(aliceEmail, alicePassword, aliceDisplayName);
        TestUser bob = await SeedUserAsync(bobEmail, bobPassword, bobDisplayName);
        Guid aliceSightingId = await alice.CreateSightingAsync(cancellationToken: cancellationToken);
        IReadOnlyList<string> needles = ResponseLeakScanner.NeedlesFor(aliceEmail);

        using HttpResponseMessage byId = await bob.Client.GetAsync($"/sightings/{aliceSightingId}", cancellationToken);
        IReadOnlyList<LeakHit> byIdLeaks = await scanner.ScanAsync(byId, needles, cancellationToken);
        string byIdBody = await byId.Content.ReadAsStringAsync(cancellationToken);
        using JsonDocument single = JsonDocument.Parse(byIdBody);

        using HttpResponseMessage nearby = await bob.Client.GetAsync(nearbyUri, cancellationToken);
        IReadOnlyList<LeakHit> nearbyLeaks = await scanner.ScanAsync(nearby, needles, cancellationToken);
        List<SightingSummary> nearbySightings = await ReadSightingsAsync(nearby);

        Assert.Equal(HttpStatusCode.OK, byId.StatusCode);
        Assert.Equal(aliceSightingId, single.RootElement.GetProperty("id").GetGuid());
        Assert.Equal(aliceDisplayName, single.RootElement.GetProperty("reporterDisplayName").GetString());
        Assert.Empty(byIdLeaks);
        Assert.Equal(HttpStatusCode.OK, nearby.StatusCode);
        Assert.Contains(new SightingSummary(aliceSightingId, aliceDisplayName), nearbySightings);
        Assert.Empty(nearbyLeaks);
    }

    [Fact]
    public async Task Open_reads_by_design_still_answer_unauthorized_to_an_anonymous_caller()
    {
        TestUser alice = await SeedUserAsync(aliceEmail, alicePassword, aliceDisplayName);
        await SeedUserAsync(bobEmail, bobPassword, bobDisplayName);
        Guid aliceSightingId = await alice.CreateSightingAsync(cancellationToken: cancellationToken);
        List<string> uris = [$"/sightings/{aliceSightingId}", nearbyUri];

        foreach (string uri in uris)
        {
            using HttpRequestMessage request = new(HttpMethod.Get, uri);
            using HttpResponseMessage response = await factory.SendFromFreshAddressAsync(request, null, cancellationToken);
            string body = await response.Content.ReadAsStringAsync(cancellationToken);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Empty(body);
        }
    }

    public void Dispose()
    {
        foreach (TestUser user in users)
            user.Dispose();

        factory.Dispose();
    }

    private async Task<TestUser> SeedUserAsync(string email, string password, string displayName)
    {
        TestUser user = await factory.CreateUserAsync(email, password, displayName, cancellationToken);
        users.Add(user);

        return user;
    }

    private async Task<HttpStatusCode> LoginAsync(string email, string password)
    {
        using HttpClient client = factory.CreateApiClient();
        using HttpResponseMessage response = await new AccountApiClient(client).LoginAsync(email, password, cancellationToken);

        return response.StatusCode;
    }

    private async Task<AccountView> ReadAccountAsync(TestUser user)
    {
        using HttpResponseMessage response = await user.Client.GetAsync("/configuration/general", cancellationToken);
        response.EnsureSuccessStatusCode();

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement currentUser = document.RootElement.GetProperty("currentUser");
        AccountView view = new(
            currentUser.GetProperty("email").GetString()!,
            currentUser.GetProperty("displayName").GetString()!);

        return view;
    }

    private async Task<List<SightingSummary>> ReadMineAsync(TestUser user)
    {
        using HttpResponseMessage response = await user.Client.GetAsync("/sightings/mine", cancellationToken);
        response.EnsureSuccessStatusCode();

        List<SightingSummary> sightings = await ReadSightingsAsync(response);

        return sightings;
    }

    private async Task<List<SightingSummary>> ReadSightingsAsync(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        using JsonDocument document = JsonDocument.Parse(body);
        List<SightingSummary> sightings = document.RootElement
            .GetProperty("items")
            .EnumerateArray()
            .Select(item => new SightingSummary(
                item.GetProperty("id").GetGuid(),
                item.GetProperty("reporterDisplayName").GetString()!))
            .ToList();

        return sightings;
    }

    private sealed record SightingSummary(Guid Id, string ReporterDisplayName);

    private sealed record AccountView(string Email, string DisplayName);
}

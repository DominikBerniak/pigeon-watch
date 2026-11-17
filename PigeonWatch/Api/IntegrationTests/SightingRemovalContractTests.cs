using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace PigeonWatch.IntegrationTests;

public sealed class SightingRemovalContractTests : IDisposable
{
    private const string aliceEmail = "alice.removal@example.org";
    private const string aliceDisplayName = "AliceRemoval";
    private const string bobEmail = "bob.removal@example.net";
    private const string bobDisplayName = "BobRemoval";
    private const string adminEmail = "admin.removal@example.com";
    private const string adminDisplayName = "AdminRemoval";
    private const string password = "Passw0rd!removal1";

    private readonly ManualTimeProvider clock = new(DateTimeOffset.UtcNow);
    private readonly PigeonWatchApiFactory factory;
    private readonly List<TestUser> users = [];
    private readonly CancellationToken cancellationToken = TestContext.Current.CancellationToken;

    public SightingRemovalContractTests()
    {
        factory = new PigeonWatchApiFactory(clock, adminEmails: [adminEmail]);
    }

    [Fact]
    public async Task Member_cannot_remove_a_sighting()
    {
        World world = await SeedAsync();
        factory.ClearLogs();

        using HttpResponseMessage remove = await RemoveAsync(world.Bob, world.SightingId);
        IReadOnlyList<FakeLogRecord> warnings = factory.Logs.GetSnapshot().Where(record => record.Level == LogLevel.Warning).ToList();
        using HttpResponseMessage restore = await RestoreAsync(world.Bob, world.SightingId);
        using HttpResponseMessage reporterRemove = await RemoveAsync(world.Alice, world.SightingId);
        JsonElement readBack = await ReadSightingAsync(world.Alice, world.SightingId);

        Assert.Equal(HttpStatusCode.Forbidden, remove.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, restore.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, reporterRemove.StatusCode);
        Assert.Single(warnings);
        Assert.Equal(JsonValueKind.Null, readBack.GetProperty("removedAt").ValueKind);
        Assert.Null(factory.ReadSightingRemovedBy(world.SightingId));
    }

    [Fact]
    public async Task Admin_removes_a_sighting_and_the_response_carries_removed_at()
    {
        World world = await SeedAsync();
        DateTime removedAt = clock.GetUtcNow().UtcDateTime;

        using HttpResponseMessage remove = await RemoveAsync(world.Admin, world.SightingId);
        string body = await remove.Content.ReadAsStringAsync(cancellationToken);
        using JsonDocument removed = JsonDocument.Parse(body);
        JsonElement readBack = await ReadSightingAsync(world.Alice, world.SightingId);

        Assert.Equal(HttpStatusCode.OK, remove.StatusCode);
        Assert.Equal(world.SightingId, removed.RootElement.GetProperty("id").GetGuid());
        Assert.Equal(removedAt, removed.RootElement.GetProperty("removedAt").GetDateTimeOffset().UtcDateTime);
        Assert.Equal(removedAt, readBack.GetProperty("removedAt").GetDateTimeOffset().UtcDateTime);
        AssertCarriesNoRemover(body, world);
    }

    [Fact]
    public async Task Removing_twice_keeps_the_first_removal()
    {
        World world = await SeedAsync();
        DateTime firstRemovedAt = clock.GetUtcNow().UtcDateTime;
        using HttpResponseMessage first = await RemoveAsync(world.Admin, world.SightingId);
        clock.Advance(TimeSpan.FromMinutes(5));

        using HttpResponseMessage second = await RemoveAsync(world.Admin, world.SightingId);
        using JsonDocument secondBody = await ReadJsonAsync(second);
        JsonElement readBack = await ReadSightingAsync(world.Alice, world.SightingId);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(firstRemovedAt, secondBody.RootElement.GetProperty("removedAt").GetDateTimeOffset().UtcDateTime);
        Assert.Equal(firstRemovedAt, readBack.GetProperty("removedAt").GetDateTimeOffset().UtcDateTime);
        Assert.Equal(world.Admin.Id, factory.ReadSightingRemovedBy(world.SightingId));
    }

    [Fact]
    public async Task Admin_restores_a_removed_sighting()
    {
        World world = await SeedAsync();
        using HttpResponseMessage remove = await RemoveAsync(world.Admin, world.SightingId);

        using HttpResponseMessage restore = await RestoreAsync(world.Admin, world.SightingId);
        using JsonDocument restored = await ReadJsonAsync(restore);
        using HttpResponseMessage restoreAgain = await RestoreAsync(world.Admin, world.SightingId);
        using JsonDocument restoredAgain = await ReadJsonAsync(restoreAgain);
        JsonElement readBack = await ReadSightingAsync(world.Alice, world.SightingId);

        Assert.Equal(HttpStatusCode.OK, remove.StatusCode);
        Assert.Equal(HttpStatusCode.OK, restore.StatusCode);
        Assert.Equal(world.SightingId, restored.RootElement.GetProperty("id").GetGuid());
        Assert.Equal(JsonValueKind.Null, restored.RootElement.GetProperty("removedAt").ValueKind);
        Assert.Equal(HttpStatusCode.OK, restoreAgain.StatusCode);
        Assert.Equal(JsonValueKind.Null, restoredAgain.RootElement.GetProperty("removedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, readBack.GetProperty("removedAt").ValueKind);
        Assert.Null(factory.ReadSightingRemovedBy(world.SightingId));
    }

    [Fact]
    public async Task Unknown_sighting_is_404_for_the_admin()
    {
        World world = await SeedAsync();
        Guid unknownId = Guid.NewGuid();

        using HttpResponseMessage remove = await RemoveAsync(world.Admin, unknownId);
        using HttpResponseMessage restore = await RestoreAsync(world.Admin, unknownId);

        Assert.Equal(HttpStatusCode.NotFound, remove.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, restore.StatusCode);
    }

    [Fact]
    public async Task Removal_records_the_admin_in_removed_by()
    {
        World world = await SeedAsync();

        using HttpResponseMessage remove = await RemoveAsync(world.Admin, world.SightingId);

        Assert.Equal(HttpStatusCode.OK, remove.StatusCode);
        Assert.Equal(world.Admin.Id, factory.ReadSightingRemovedBy(world.SightingId));
    }

    public void Dispose()
    {
        foreach (TestUser user in users)
            user.Dispose();

        factory.Dispose();
    }

    private static void AssertCarriesNoRemover(string body, World world)
    {
        string[] forbidden =
        [
            "removedBy",
            "reporterId",
            "updateUser",
            world.Admin.Id.ToString(),
            world.Alice.Id.ToString(),
            world.Admin.Email,
            world.Alice.Email
        ];

        foreach (string needle in forbidden)
            Assert.DoesNotContain(needle, body, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<HttpResponseMessage> RemoveAsync(TestUser user, Guid sightingId)
    {
        HttpResponseMessage response = await user.Client.DeleteAsync($"/sightings/{sightingId}", cancellationToken);

        return response;
    }

    private async Task<HttpResponseMessage> RestoreAsync(TestUser user, Guid sightingId)
    {
        HttpResponseMessage response = await user.Client.PostAsync($"/sightings/{sightingId}/restore", null, cancellationToken);

        return response;
    }

    private async Task<JsonElement> ReadSightingAsync(TestUser user, Guid sightingId)
    {
        using HttpResponseMessage response = await user.Client.GetAsync($"/sightings/{sightingId}", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using JsonDocument document = await ReadJsonAsync(response);

        return document.RootElement.Clone();
    }

    private async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        JsonDocument document = JsonDocument.Parse(body);

        return document;
    }

    private async Task<World> SeedAsync()
    {
        TestUser alice = Track(await factory.CreateUserAsync(aliceEmail, password, aliceDisplayName, cancellationToken));
        TestUser bob = Track(await factory.CreateUserAsync(bobEmail, password, bobDisplayName, cancellationToken));
        TestUser admin = Track(await factory.CreateAdminAsync(adminEmail, password, adminDisplayName, cancellationToken));
        Guid sightingId = await alice.CreateSightingAsync(cancellationToken: cancellationToken);

        return new World(alice, bob, admin, sightingId);
    }

    private TestUser Track(TestUser user)
    {
        users.Add(user);

        return user;
    }

    private sealed record World(TestUser Alice, TestUser Bob, TestUser Admin, Guid SightingId);
}

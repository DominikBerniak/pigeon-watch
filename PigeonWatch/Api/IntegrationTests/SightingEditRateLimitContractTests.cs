using System.Net;

namespace PigeonWatch.IntegrationTests;

public sealed class SightingEditRateLimitContractTests : IDisposable
{
    private const string password = "Passw0rd!limit1";
    private const int defaultPermitLimit = 10;

    private readonly PigeonWatchApiFactory factory = new(sightingEditPermitLimit: null);
    private readonly List<TestUser> users = [];
    private readonly CancellationToken cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Eleventh_edit_by_one_user_in_a_minute_is_429_with_retry_after_while_another_user_still_edits()
    {
        TestUser alice = await SeedUserAsync("alice.limit@example.org", "AliceLimit");
        TestUser bob = await SeedUserAsync("bob.limit@example.net", "BobLimit");
        Guid aliceSightingId = await alice.CreateSightingAsync(cancellationToken: cancellationToken);
        Guid bobSightingId = await bob.CreateSightingAsync(cancellationToken: cancellationToken);

        for (int attemptNumber = 0; attemptNumber < defaultPermitLimit; attemptNumber++)
        {
            using HttpResponseMessage edit = await alice.UpdateSightingAsync(aliceSightingId, SightingForm.UpdateFields(name: $"Edit {attemptNumber}"), cancellationToken: cancellationToken);

            Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        }

        using HttpResponseMessage eleventh = await alice.UpdateSightingAsync(aliceSightingId, SightingForm.UpdateFields(name: "Edit 11"), cancellationToken: cancellationToken);
        string eleventhBody = await eleventh.Content.ReadAsStringAsync(cancellationToken);
        using HttpRequestMessage fromOtherAddress = SightingForm.UpdateRequest(aliceSightingId, SightingForm.UpdateFields(name: "Edit 12"));
        using HttpResponseMessage otherAddress = await factory.SendFromFreshAddressAsync(fromOtherAddress, alice.AccessToken, cancellationToken);
        using HttpResponseMessage bobEdit = await bob.UpdateSightingAsync(bobSightingId, SightingForm.UpdateFields(name: "Bob edit"), cancellationToken: cancellationToken);

        Assert.Equal(HttpStatusCode.TooManyRequests, eleventh.StatusCode);
        Assert.NotNull(eleventh.Headers.RetryAfter?.Delta);
        Assert.InRange(eleventh.Headers.RetryAfter.Delta.Value, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(60));
        Assert.Equal(string.Empty, eleventhBody);
        Assert.Equal(HttpStatusCode.TooManyRequests, otherAddress.StatusCode);
        Assert.Equal(HttpStatusCode.OK, bobEdit.StatusCode);
        Assert.Contains(
            factory.Logs.GetSnapshot(),
            record => record.Category == "PigeonWatch.RateLimiting" && record.Message.Contains("sightingEdits", StringComparison.Ordinal));
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

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PigeonWatch.Data.Identity;

namespace PigeonWatch.IntegrationTests;

public sealed class AdminRoleContractTests : IDisposable
{
    private const string adminEmail = "admin.role@example.org";
    private const string adminDisplayName = "AdminRole";
    private const string memberEmail = "member.role@example.net";
    private const string memberDisplayName = "MemberRole";
    private const string password = "Passw0rd!admin1";

    private readonly ManualTimeProvider clock = new(DateTimeOffset.UtcNow);
    private readonly PigeonWatchApiFactory factory;
    private readonly List<TestUser> users = [];
    private readonly CancellationToken cancellationToken = TestContext.Current.CancellationToken;

    public AdminRoleContractTests()
    {
        factory = new PigeonWatchApiFactory(clock, adminEmails: [adminEmail]);
    }

    [Fact]
    public async Task Admin_listed_in_configuration_gets_the_admin_role_in_general_configuration()
    {
        TestUser admin = Track(await factory.CreateAdminAsync(adminEmail, password, adminDisplayName, cancellationToken));

        IReadOnlyList<string> roles = await factory.ReadRolesAsync(admin.AccessToken, cancellationToken);

        Assert.Equal(["Admin"], roles);
    }

    [Fact]
    public async Task Member_not_listed_has_no_roles()
    {
        TestUser member = Track(await factory.CreateUserAsync(memberEmail, password, memberDisplayName, cancellationToken));
        Track(await factory.CreateAdminAsync(adminEmail, password, adminDisplayName, cancellationToken));

        string refreshedAccessToken = await RefreshAccessTokenAsync(member.RefreshToken);
        IReadOnlyList<string> roles = await factory.ReadRolesAsync(refreshedAccessToken, cancellationToken);

        Assert.Empty(roles);
    }

    [Fact]
    public async Task Role_granted_after_login_appears_after_token_refresh()
    {
        TestUser listed = Track(await factory.CreateUserAsync(adminEmail, password, adminDisplayName, cancellationToken));
        IReadOnlyList<string> rolesBeforeGrant = await factory.ReadRolesAsync(listed.AccessToken, cancellationToken);

        await factory.Services.GetRequiredService<AdminRoleBootstrapper>().RunAsync(cancellationToken);
        IReadOnlyList<string> rolesOnTheOldToken = await factory.ReadRolesAsync(listed.AccessToken, cancellationToken);
        string refreshedAccessToken = await RefreshAccessTokenAsync(listed.RefreshToken);
        IReadOnlyList<string> rolesAfterRefresh = await factory.ReadRolesAsync(refreshedAccessToken, cancellationToken);

        Assert.Empty(rolesBeforeGrant);
        Assert.Empty(rolesOnTheOldToken);
        Assert.Equal(["Admin"], rolesAfterRefresh);
    }

    public void Dispose()
    {
        foreach (TestUser user in users)
            user.Dispose();

        factory.Dispose();
    }

    private TestUser Track(TestUser user)
    {
        users.Add(user);

        return user;
    }

    private async Task<string> RefreshAccessTokenAsync(string refreshToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, "/auth/refresh")
        {
            Content = JsonContent.Create(new { refreshToken })
        };
        using HttpResponseMessage response = await factory.SendFromFreshAddressAsync(request, null, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        using JsonDocument document = JsonDocument.Parse(body);
        string accessToken = document.RootElement.GetProperty("accessToken").GetString()!;

        return accessToken;
    }
}

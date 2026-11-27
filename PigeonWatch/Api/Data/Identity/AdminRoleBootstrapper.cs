using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PigeonWatch.BusinessObjects;

namespace PigeonWatch.Data.Identity;

public class AdminRoleBootstrapper(
    IServiceScopeFactory scopeFactory,
    IOptions<ModerationOptions> options,
    ILogger<AdminRoleBootstrapper> logger)
{
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await using (AsyncServiceScope roleScope = scopeFactory.CreateAsyncScope())
        {
            RoleManager<ApplicationRole> roleManager = roleScope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();

            await EnsureAdminRoleAsync(roleManager, cancellationToken);
        }

        List<string> adminEmails = options.Value.AdminEmails;

        for (int index = 0; index < adminEmails.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await GrantAdminInOwnScopeAsync(adminEmails[index], index, cancellationToken);
        }
    }

    private async Task GrantAdminInOwnScopeAsync(string email, int index, CancellationToken cancellationToken = default)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        UserManager<ApplicationUser> userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        try
        {
            await GrantAdminAsync(userManager, email, index, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(
                exception,
                "Granting the {RoleName} role to the moderation admin email at index {AdminEmailIndex} failed; the remaining emails are still processed",
                RoleNames.Admin,
                index);
        }
    }

    private async Task EnsureAdminRoleAsync(RoleManager<ApplicationRole> roleManager, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (await roleManager.RoleExistsAsync(RoleNames.Admin))
            return;

        IdentityResult result = await roleManager.CreateAsync(new ApplicationRole { Name = RoleNames.Admin });

        if (!result.Succeeded)
        {
            string codes = string.Join(", ", result.Errors.Select(error => error.Code));

            throw new InvalidOperationException($"Creating the {RoleNames.Admin} role failed with {codes}.");
        }

        logger.LogInformation("Created the {RoleName} role", RoleNames.Admin);
    }

    private async Task GrantAdminAsync(UserManager<ApplicationUser> userManager, string email, int index, CancellationToken cancellationToken = default)
    {
        ApplicationUser? user = string.IsNullOrWhiteSpace(email) ? null : await userManager.FindByEmailAsync(email.Trim());

        if (user is null)
        {
            logger.LogWarning(
                "Moderation admin email at index {AdminEmailIndex} has no registered user, so the {RoleName} role was not granted",
                index,
                RoleNames.Admin);

            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (await userManager.IsInRoleAsync(user, RoleNames.Admin))
            return;

        IdentityResult result = await userManager.AddToRoleAsync(user, RoleNames.Admin);

        if (!result.Succeeded)
        {
            logger.LogError(
                "Granting the {RoleName} role to user {UserId} failed with {ErrorCodes}",
                RoleNames.Admin,
                user.Id,
                string.Join(", ", result.Errors.Select(error => error.Code)));

            return;
        }

        logger.LogInformation("Granted the {RoleName} role to user {UserId}", RoleNames.Admin, user.Id);
    }
}

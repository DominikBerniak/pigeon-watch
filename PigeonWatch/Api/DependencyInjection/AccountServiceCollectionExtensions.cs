using Microsoft.Extensions.DependencyInjection;
using PigeonWatch.BusinessObjects;
using PigeonWatch.Data.Identity;

namespace PigeonWatch.DependencyInjection;

public static class AccountServiceCollectionExtensions
{
    public static IServiceCollection AddAccounts(this IServiceCollection services)
    {
        services
            .AddIdentityApiEndpoints<ApplicationUser>(options =>
            {
                options.Password.RequireDigit = AccountRules.PasswordRequiresDigit;
                options.Password.RequireLowercase = AccountRules.PasswordRequiresLowercase;
                options.Password.RequireUppercase = AccountRules.PasswordRequiresUppercase;
                options.Password.RequireNonAlphanumeric = AccountRules.PasswordRequiresNonAlphanumeric;
                options.Password.RequiredLength = AccountRules.PasswordMinLength;

                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
                options.Lockout.AllowedForNewUsers = true;

                options.User.RequireUniqueEmail = true;
                options.User.AllowedUserNameCharacters = string.Empty;

                options.SignIn.RequireConfirmedEmail = false;
            })
            .AddUserStore<PigeonWatchUserStore>();

        return services;
    }
}

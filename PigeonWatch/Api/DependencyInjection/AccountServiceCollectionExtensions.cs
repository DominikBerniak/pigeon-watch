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
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;
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

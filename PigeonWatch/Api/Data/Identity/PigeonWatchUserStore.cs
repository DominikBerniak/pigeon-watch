using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PigeonWatch.BusinessObjects;
using PigeonWatch.Data.Entities;
using PigeonWatch.Data.Mappers;

namespace PigeonWatch.Data.Identity;

public class PigeonWatchUserStore(
    PigeonWatchDbContext db,
    IUserAccountMapper userAccountMapper,
    ILookupNormalizer lookupNormalizer,
    IdentityErrorDescriber errorDescriber) :
    IUserPasswordStore<ApplicationUser>,
    IUserEmailStore<ApplicationUser>,
    IUserSecurityStampStore<ApplicationUser>,
    IUserLockoutStore<ApplicationUser>
{
    private const int duplicateKeyRow = 2601;
    private const int uniqueConstraintViolation = 2627;
    private const string emailIndexName = "IX_USER_ACCOUNT_NORMALIZED_EMAIL";
    private const string userNameIndexName = "IX_USER_ACCOUNT_NORMALIZED_USER_NAME";
    private const string displayNameIndexName = "IX_USER_ACCOUNT_NORMALIZED_DISPLAY_NAME";

    public async Task<IdentityResult> CreateAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        string normalizedDisplayName = NormalizeDisplayName(user);

        if (await IsDisplayNameTakenAsync(normalizedDisplayName, user.Id, cancellationToken))
            return IdentityResult.Failed(DuplicateDisplayName(user));

        UserAccountEntity entity = new();
        userAccountMapper.CopyToEntity(user, entity);
        entity.NormalizedDisplayName = normalizedDisplayName;
        db.UserAccounts.Add(entity);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (DuplicateIndexError(exception, user) is IdentityError duplicate)
        {
            db.Entry(entity).State = EntityState.Detached;

            return IdentityResult.Failed(duplicate);
        }

        user.Id = entity.Id;

        return IdentityResult.Success;
    }

    public async Task<IdentityResult> UpdateAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        UserAccountEntity? entity = await db.UserAccounts.FindAsync([user.Id], cancellationToken);

        if (entity is null)
            return IdentityResult.Failed(errorDescriber.ConcurrencyFailure());

        string normalizedDisplayName = NormalizeDisplayName(user);

        if (await IsDisplayNameTakenAsync(normalizedDisplayName, user.Id, cancellationToken))
            return IdentityResult.Failed(DuplicateDisplayName(user));

        string concurrencyStamp = Guid.NewGuid().ToString();
        db.Entry(entity).Property(e => e.ConcurrencyStamp).OriginalValue = user.ConcurrencyStamp;
        userAccountMapper.CopyToEntity(user, entity);
        entity.NormalizedDisplayName = normalizedDisplayName;
        entity.ConcurrencyStamp = concurrencyStamp;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.Entry(entity).State = EntityState.Detached;

            return IdentityResult.Failed(errorDescriber.ConcurrencyFailure());
        }
        catch (DbUpdateException exception) when (DuplicateIndexError(exception, user) is IdentityError duplicate)
        {
            db.Entry(entity).State = EntityState.Detached;

            return IdentityResult.Failed(duplicate);
        }

        user.ConcurrencyStamp = concurrencyStamp;

        return IdentityResult.Success;
    }

    public async Task<IdentityResult> DeleteAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        UserAccountEntity? entity = await db.UserAccounts.FindAsync([user.Id], cancellationToken);

        if (entity is null)
            return IdentityResult.Failed(errorDescriber.ConcurrencyFailure());

        db.Entry(entity).Property(e => e.ConcurrencyStamp).OriginalValue = user.ConcurrencyStamp;
        db.UserAccounts.Remove(entity);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.Entry(entity).State = EntityState.Detached;

            return IdentityResult.Failed(errorDescriber.ConcurrencyFailure());
        }

        return IdentityResult.Success;
    }

    public async Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(userId, out Guid id))
            return null;

        UserAccountEntity? entity = await db.UserAccounts
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == id, cancellationToken);

        return entity is null ? null : userAccountMapper.ToUser(entity);
    }

    public async Task<ApplicationUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken = default)
    {
        UserAccountEntity? entity = await db.UserAccounts
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.NormalizedUserName == normalizedUserName, cancellationToken);

        return entity is null ? null : userAccountMapper.ToUser(entity);
    }

    public async Task<ApplicationUser?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default)
    {
        UserAccountEntity? entity = await db.UserAccounts
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken);

        return entity is null ? null : userAccountMapper.ToUser(entity);
    }

    public Task<string> GetUserIdAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(user.Id.ToString());
    }

    public Task<string?> GetUserNameAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<string?>(user.UserName);
    }

    public Task SetUserNameAsync(ApplicationUser user, string? userName, CancellationToken cancellationToken = default)
    {
        user.UserName = userName ?? string.Empty;

        return Task.CompletedTask;
    }

    public Task<string?> GetNormalizedUserNameAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<string?>(user.NormalizedUserName);
    }

    public Task SetNormalizedUserNameAsync(ApplicationUser user, string? normalizedName, CancellationToken cancellationToken = default)
    {
        user.NormalizedUserName = normalizedName ?? string.Empty;

        return Task.CompletedTask;
    }

    public Task SetPasswordHashAsync(ApplicationUser user, string? passwordHash, CancellationToken cancellationToken = default)
    {
        user.PasswordHash = passwordHash ?? string.Empty;

        return Task.CompletedTask;
    }

    public Task<string?> GetPasswordHashAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<string?>(string.IsNullOrEmpty(user.PasswordHash) ? null : user.PasswordHash);
    }

    public Task<bool> HasPasswordAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(!string.IsNullOrEmpty(user.PasswordHash));
    }

    public Task SetEmailAsync(ApplicationUser user, string? email, CancellationToken cancellationToken = default)
    {
        user.Email = email ?? string.Empty;

        return Task.CompletedTask;
    }

    public Task<string?> GetEmailAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<string?>(user.Email);
    }

    public Task<bool> GetEmailConfirmedAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }

    public Task SetEmailConfirmedAsync(ApplicationUser user, bool confirmed, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<string?> GetNormalizedEmailAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<string?>(user.NormalizedEmail);
    }

    public Task SetNormalizedEmailAsync(ApplicationUser user, string? normalizedEmail, CancellationToken cancellationToken = default)
    {
        user.NormalizedEmail = normalizedEmail ?? string.Empty;

        return Task.CompletedTask;
    }

    public Task SetSecurityStampAsync(ApplicationUser user, string stamp, CancellationToken cancellationToken = default)
    {
        user.SecurityStamp = stamp;

        return Task.CompletedTask;
    }

    public Task<string?> GetSecurityStampAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<string?>(user.SecurityStamp);
    }

    public Task<DateTimeOffset?> GetLockoutEndDateAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(user.LockoutEnd);
    }

    public Task SetLockoutEndDateAsync(ApplicationUser user, DateTimeOffset? lockoutEnd, CancellationToken cancellationToken = default)
    {
        user.LockoutEnd = lockoutEnd;

        return Task.CompletedTask;
    }

    public Task<int> IncrementAccessFailedCountAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        user.AccessFailedCount++;

        return Task.FromResult(user.AccessFailedCount);
    }

    public Task ResetAccessFailedCountAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        user.AccessFailedCount = 0;

        return Task.CompletedTask;
    }

    public Task<int> GetAccessFailedCountAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(user.AccessFailedCount);
    }

    public Task<bool> GetLockoutEnabledAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(user.LockoutEnabled);
    }

    public Task SetLockoutEnabledAsync(ApplicationUser user, bool enabled, CancellationToken cancellationToken = default)
    {
        user.LockoutEnabled = enabled;

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    private string NormalizeDisplayName(ApplicationUser user)
    {
        return lookupNormalizer.NormalizeName(user.DisplayName) ?? string.Empty;
    }

    private Task<bool> IsDisplayNameTakenAsync(string normalizedDisplayName, Guid userId, CancellationToken cancellationToken = default)
    {
        return db.UserAccounts.AnyAsync(u => u.NormalizedDisplayName == normalizedDisplayName && u.Id != userId, cancellationToken);
    }

    private IdentityError? DuplicateIndexError(DbUpdateException exception, ApplicationUser user)
    {
        if (exception.InnerException is not SqlException { Number: duplicateKeyRow or uniqueConstraintViolation } sqlException)
            return null;

        string message = sqlException.Message;

        if (message.Contains(displayNameIndexName, StringComparison.Ordinal))
            return DuplicateDisplayName(user);

        if (message.Contains(emailIndexName, StringComparison.Ordinal))
            return errorDescriber.DuplicateEmail(user.Email);

        if (message.Contains(userNameIndexName, StringComparison.Ordinal))
            return errorDescriber.DuplicateUserName(user.UserName);

        return null;
    }

    private static IdentityError DuplicateDisplayName(ApplicationUser user)
    {
        return new()
        {
            Code = AccountErrorCodes.DuplicateDisplayName,
            Description = $"Display name '{user.DisplayName}' is already taken."
        };
    }
}

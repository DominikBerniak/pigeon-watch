using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PigeonWatch.Data.Entities;
using PigeonWatch.Data.Mappers;

namespace PigeonWatch.Data.Identity;

public class PigeonWatchRoleStore(
    PigeonWatchDbContext db,
    IRoleMapper roleMapper,
    IdentityErrorDescriber errorDescriber,
    ILogger<PigeonWatchRoleStore> logger) : IRoleStore<ApplicationRole>
{
    public async Task<IdentityResult> CreateAsync(ApplicationRole role, CancellationToken cancellationToken = default)
    {
        RoleEntity entity = new();
        roleMapper.CopyToEntity(role, entity);
        db.Roles.Add(entity);

        await db.SaveChangesAsync(cancellationToken);

        role.Id = entity.Id;

        return IdentityResult.Success;
    }

    public async Task<IdentityResult> UpdateAsync(ApplicationRole role, CancellationToken cancellationToken = default)
    {
        RoleEntity? entity = await db.Roles.FindAsync([role.Id], cancellationToken);

        if (entity is null)
        {
            LogConcurrencyFailure("update", role.Id, null);

            return IdentityResult.Failed(errorDescriber.ConcurrencyFailure());
        }

        string concurrencyStamp = Guid.NewGuid().ToString();
        db.Entry(entity).Property(e => e.ConcurrencyStamp).OriginalValue = role.ConcurrencyStamp;
        roleMapper.CopyToEntity(role, entity);
        entity.ConcurrencyStamp = concurrencyStamp;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            LogConcurrencyFailure("update", role.Id, exception);

            db.Entry(entity).State = EntityState.Detached;

            return IdentityResult.Failed(errorDescriber.ConcurrencyFailure());
        }

        role.ConcurrencyStamp = concurrencyStamp;

        return IdentityResult.Success;
    }

    public async Task<IdentityResult> DeleteAsync(ApplicationRole role, CancellationToken cancellationToken = default)
    {
        RoleEntity? entity = await db.Roles.FindAsync([role.Id], cancellationToken);

        if (entity is null)
        {
            LogConcurrencyFailure("delete", role.Id, null);

            return IdentityResult.Failed(errorDescriber.ConcurrencyFailure());
        }

        db.Entry(entity).Property(e => e.ConcurrencyStamp).OriginalValue = role.ConcurrencyStamp;
        db.Roles.Remove(entity);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            LogConcurrencyFailure("delete", role.Id, exception);

            db.Entry(entity).State = EntityState.Detached;

            return IdentityResult.Failed(errorDescriber.ConcurrencyFailure());
        }

        return IdentityResult.Success;
    }

    public async Task<ApplicationRole?> FindByIdAsync(string roleId, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(roleId, out Guid id))
            return null;

        RoleEntity? entity = await db.Roles
            .AsNoTracking()
            .SingleOrDefaultAsync(r => r.Id == id, cancellationToken);

        return entity is null ? null : roleMapper.ToRole(entity);
    }

    public async Task<ApplicationRole?> FindByNameAsync(string normalizedRoleName, CancellationToken cancellationToken = default)
    {
        RoleEntity? entity = await db.Roles
            .AsNoTracking()
            .SingleOrDefaultAsync(r => r.NormalizedName == normalizedRoleName, cancellationToken);

        return entity is null ? null : roleMapper.ToRole(entity);
    }

    public Task<string> GetRoleIdAsync(ApplicationRole role, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(role.Id.ToString());
    }

    public Task<string?> GetRoleNameAsync(ApplicationRole role, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<string?>(role.Name);
    }

    public Task SetRoleNameAsync(ApplicationRole role, string? roleName, CancellationToken cancellationToken = default)
    {
        role.Name = roleName ?? string.Empty;

        return Task.CompletedTask;
    }

    public Task<string?> GetNormalizedRoleNameAsync(ApplicationRole role, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<string?>(role.NormalizedName);
    }

    public Task SetNormalizedRoleNameAsync(ApplicationRole role, string? normalizedName, CancellationToken cancellationToken = default)
    {
        role.NormalizedName = normalizedName ?? string.Empty;

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    private void LogConcurrencyFailure(string operation, Guid roleId, Exception? exception)
    {
        logger.LogWarning(exception, "Role store {Operation} hit a concurrency failure or missing role {RoleId}", operation, roleId);
    }
}

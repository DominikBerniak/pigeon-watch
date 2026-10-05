using PigeonWatch.BusinessObjects;
using PigeonWatch.Data.Entities;
using PigeonWatch.Data.Identity;

namespace PigeonWatch.Data.Mappers;

public class UserAccountMapper : IUserAccountMapper
{
    ApplicationUser IUserAccountMapper.ToUser(UserAccountEntity entity)
    {
        return new()
        {
            Id = entity.Id,
            Email = entity.Email,
            NormalizedEmail = entity.NormalizedEmail,
            UserName = entity.UserName,
            NormalizedUserName = entity.NormalizedUserName,
            DisplayName = entity.DisplayName,
            PasswordHash = entity.PasswordHash,
            SecurityStamp = entity.SecurityStamp,
            ConcurrencyStamp = entity.ConcurrencyStamp,
            LockoutEnd = entity.LockoutEnd,
            LockoutEnabled = entity.LockoutEnabled,
            AccessFailedCount = entity.AccessFailedCount
        };
    }

    void IUserAccountMapper.CopyToEntity(ApplicationUser user, UserAccountEntity entity)
    {
        entity.Email = user.Email;
        entity.NormalizedEmail = user.NormalizedEmail;
        entity.UserName = user.UserName;
        entity.NormalizedUserName = user.NormalizedUserName;
        entity.DisplayName = user.DisplayName;
        entity.PasswordHash = user.PasswordHash;
        entity.SecurityStamp = user.SecurityStamp;
        entity.ConcurrencyStamp = user.ConcurrencyStamp;
        entity.LockoutEnd = user.LockoutEnd;
        entity.LockoutEnabled = user.LockoutEnabled;
        entity.AccessFailedCount = user.AccessFailedCount;
    }

    RegisteredAccount IUserAccountMapper.ToRegisteredAccount(ApplicationUser user)
    {
        return new RegisteredAccount(user.Email, user.DisplayName);
    }

    CurrentUser IUserAccountMapper.ToCurrentUser(ApplicationUser user)
    {
        return new CurrentUser(user.Id, user.Email, user.DisplayName);
    }
}

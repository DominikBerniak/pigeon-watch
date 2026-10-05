using PigeonWatch.BusinessObjects;
using PigeonWatch.Data.Entities;
using PigeonWatch.Data.Identity;

namespace PigeonWatch.Data.Mappers;

public interface IUserAccountMapper
{
    internal ApplicationUser ToUser(UserAccountEntity entity);

    internal void CopyToEntity(ApplicationUser user, UserAccountEntity entity);

    RegisteredAccount ToRegisteredAccount(ApplicationUser user);

    CurrentUser ToCurrentUser(ApplicationUser user);
}

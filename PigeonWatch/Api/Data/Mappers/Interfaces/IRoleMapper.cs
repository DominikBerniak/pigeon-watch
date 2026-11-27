using PigeonWatch.Data.Entities;
using PigeonWatch.Data.Identity;

namespace PigeonWatch.Data.Mappers;

public interface IRoleMapper
{
    internal ApplicationRole ToRole(RoleEntity entity);
    internal void CopyToEntity(ApplicationRole role, RoleEntity entity);
}

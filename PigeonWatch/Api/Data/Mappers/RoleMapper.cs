using PigeonWatch.Data.Entities;
using PigeonWatch.Data.Identity;

namespace PigeonWatch.Data.Mappers;

public class RoleMapper : IRoleMapper
{
    ApplicationRole IRoleMapper.ToRole(RoleEntity entity)
    {
        return new()
        {
            Id = entity.Id,
            Name = entity.Name,
            NormalizedName = entity.NormalizedName,
            ConcurrencyStamp = entity.ConcurrencyStamp
        };
    }

    void IRoleMapper.CopyToEntity(ApplicationRole role, RoleEntity entity)
    {
        entity.Name = role.Name;
        entity.NormalizedName = role.NormalizedName;
        entity.ConcurrencyStamp = role.ConcurrencyStamp;
    }
}

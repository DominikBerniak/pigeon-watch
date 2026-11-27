namespace PigeonWatch.Data.Entities;

internal class UserRoleEntity : AuditableEntity
{
    public Guid UserId { get; set; }

    public UserAccountEntity User { get; set; } = null!;

    public Guid RoleId { get; set; }

    public RoleEntity Role { get; set; } = null!;
}

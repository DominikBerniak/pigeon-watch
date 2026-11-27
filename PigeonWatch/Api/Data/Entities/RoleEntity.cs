using System.ComponentModel.DataAnnotations;

namespace PigeonWatch.Data.Entities;

internal class RoleEntity : AuditableEntity
{
    [MaxLength(256)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(256)]
    public string NormalizedName { get; set; } = string.Empty;

    public string ConcurrencyStamp { get; set; } = string.Empty;
}

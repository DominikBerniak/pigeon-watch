using System.ComponentModel.DataAnnotations;

namespace PigeonWatch.Data.Entities;

internal abstract class AuditableEntity : Entity
{
    [MaxLength(128)]
    public string CreateUser { get; set; } = string.Empty;

    public DateTime CreateDate { get; set; }

    [MaxLength(128)]
    public string UpdateUser { get; set; } = string.Empty;

    public DateTime UpdateDate { get; set; }
}

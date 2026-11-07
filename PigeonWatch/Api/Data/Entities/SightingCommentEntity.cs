using System.ComponentModel.DataAnnotations;
using PigeonWatch.BusinessObjects;

namespace PigeonWatch.Data.Entities;

internal class SightingCommentEntity : AuditableEntity
{
    public Guid SightingId { get; set; }

    public SightingEntity Sighting { get; set; } = null!;

    public Guid AuthorId { get; set; }

    public UserAccountEntity Author { get; set; } = null!;

    [MaxLength(SightingCommentRules.TextMaxLength)]
    public string Text { get; set; } = string.Empty;

    public DateTime? EditedAt { get; set; }

    public DateTime? DeletedAt { get; set; }
}

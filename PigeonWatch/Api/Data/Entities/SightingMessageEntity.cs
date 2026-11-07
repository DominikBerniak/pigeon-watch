using System.ComponentModel.DataAnnotations;
using PigeonWatch.BusinessObjects;

namespace PigeonWatch.Data.Entities;

internal class SightingMessageEntity : AuditableEntity
{
    public Guid ThreadId { get; set; }

    public SightingThreadEntity Thread { get; set; } = null!;

    public Guid SenderId { get; set; }

    public UserAccountEntity Sender { get; set; } = null!;

    [MaxLength(SightingMessageRules.TextMaxLength)]
    public string Text { get; set; } = string.Empty;
}

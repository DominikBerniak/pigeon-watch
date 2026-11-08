namespace PigeonWatch.Data.Entities;

internal class SightingThreadEntity : AuditableEntity
{
    public Guid SightingId { get; set; }

    public SightingEntity Sighting { get; set; } = null!;

    public Guid ParticipantId { get; set; }

    public UserAccountEntity Participant { get; set; } = null!;

    public DateTime LastMessageAt { get; set; }

    public DateTime? OwnerLastReadAt { get; set; }

    public DateTime? ParticipantLastReadAt { get; set; }

    public List<SightingMessageEntity> Messages { get; set; } = [];
}

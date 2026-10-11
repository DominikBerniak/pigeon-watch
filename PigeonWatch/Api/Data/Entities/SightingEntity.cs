using System.ComponentModel.DataAnnotations;
using PigeonWatch.BusinessObjects;

namespace PigeonWatch.Data.Entities;

internal class SightingEntity : AuditableEntity
{
    public Guid ReporterId { get; set; }

    public UserAccountEntity Reporter { get; set; } = null!;

    [MaxLength(SightingRules.NameMaxLength)]
    public string? Name { get; set; }

    [MaxLength(SightingRules.DescriptionMaxLength)]
    public string Description { get; set; } = string.Empty;

    [MaxLength(SightingRules.BehaviourMaxLength)]
    public string Behaviour { get; set; } = string.Empty;

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    public SightingStatus Status { get; set; }

    public SightingCriticality Criticality { get; set; }
}

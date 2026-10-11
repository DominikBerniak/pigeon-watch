using System.ComponentModel.DataAnnotations;
using PigeonWatch.BusinessObjects;

namespace PigeonWatch.WebApi.Models;

public sealed class CreateSightingRequestModel
{
    [MaxLength(SightingRules.NameMaxLength)]
    public string? Name { get; init; }

    [Required]
    [MaxLength(SightingRules.DescriptionMaxLength)]
    public required string Description { get; init; }

    [Required]
    [MaxLength(SightingRules.BehaviourMaxLength)]
    public required string Behaviour { get; init; }

    public required double Latitude { get; init; }

    public required double Longitude { get; init; }

    public required int Status { get; init; }

    public required int Criticality { get; init; }
}

using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
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

    [BindRequired]
    public required double Latitude { get; init; }

    [BindRequired]
    public required double Longitude { get; init; }

    [BindRequired]
    public required int Status { get; init; }

    [BindRequired]
    public required int Criticality { get; init; }

    public IFormFile? Photo { get; init; }
}

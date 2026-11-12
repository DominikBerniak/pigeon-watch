using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using PigeonWatch.BusinessObjects;

namespace PigeonWatch.WebApi.Models;

public sealed class UpdateSightingRequestModel
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
    public required int Status { get; init; }

    [BindRequired]
    public required int Criticality { get; init; }

    public IFormFile? Photo { get; init; }

    public bool RemovePhoto { get; init; }
}

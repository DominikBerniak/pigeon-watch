using System.ComponentModel.DataAnnotations;

namespace PigeonWatch.WebApi.Models;

public sealed class SightingThreadReadRequestModel
{
    [Required]
    public DateTimeOffset? SeenAt { get; init; }
}

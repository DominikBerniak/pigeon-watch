using System.ComponentModel.DataAnnotations;
using PigeonWatch.BusinessObjects;

namespace PigeonWatch.WebApi.Models;

public sealed class SightingMessageRequestModel
{
    [Required(AllowEmptyStrings = true)]
    [MaxLength(SightingMessageRules.TextMaxLength)]
    public required string Text { get; init; }
}

using System.ComponentModel.DataAnnotations;
using PigeonWatch.BusinessObjects;

namespace PigeonWatch.WebApi.Models;

public sealed class SightingCommentRequestModel
{
    [Required(AllowEmptyStrings = true)]
    [MaxLength(SightingCommentRules.TextMaxLength)]
    public required string Text { get; init; }
}

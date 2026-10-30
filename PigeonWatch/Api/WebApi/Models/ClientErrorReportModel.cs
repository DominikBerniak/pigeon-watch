using System.ComponentModel.DataAnnotations;

namespace PigeonWatch.WebApi.Models;

public sealed class ClientErrorReportModel
{
    [Required]
    [AllowedValues("uncaught", "bootstrap", "navigation", "api-failure", "session-expired", "warmup-exhausted", "fallback")]
    public required string Kind { get; init; }

    [Required]
    [MaxLength(60)]
    [RegularExpression(@"^[a-z][a-z0-9]*(\.[a-z0-9]+)*\z")]
    public required string Context { get; init; }

    [Range(0, 599)]
    public int? HttpStatus { get; init; }

    [MaxLength(80)]
    [RegularExpression(@"^[A-Za-z_][A-Za-z0-9_]*\z")]
    public string? ErrorName { get; init; }

    [MaxLength(64)]
    [RegularExpression(@"^[0-9a-fA-F-]+\z")]
    public string? TraceId { get; init; }

    [Required]
    [MaxLength(100)]
    [RegularExpression(@"^/[A-Za-z0-9/:_.-]*\z")]
    public required string Route { get; init; }

    public required Guid SessionId { get; init; }
}

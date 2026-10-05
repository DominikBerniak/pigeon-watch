namespace PigeonWatch.WebApi.Models;

public sealed class DisplayNameRulesModel
{
    public required int MinLength { get; init; }

    public required int MaxLength { get; init; }
}

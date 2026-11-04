namespace PigeonWatch.WebApi.Models;

public sealed class SightingRulesModel
{
    public required int NameMaxLength { get; init; }

    public required int DescriptionMaxLength { get; init; }

    public required int BehaviourMaxLength { get; init; }

    public required int CooldownSeconds { get; init; }

    public required int NearbyRadiusKm { get; init; }

    public required int PhotoMaxBytes { get; init; }

    public required int PhotoMaxDimension { get; init; }

    public required IReadOnlyList<string> PhotoAllowedContentTypes { get; init; }
}

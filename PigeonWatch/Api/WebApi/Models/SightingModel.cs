namespace PigeonWatch.WebApi.Models;

public sealed class SightingModel
{
    public required Guid Id { get; init; }

    public required string? Name { get; init; }

    public required string Description { get; init; }

    public required string Behaviour { get; init; }

    public required double Latitude { get; init; }

    public required double Longitude { get; init; }

    public required int Status { get; init; }

    public required int Criticality { get; init; }

    public required string ReporterDisplayName { get; init; }

    public required DateTime CreatedAt { get; init; }
}

namespace PigeonWatch.BusinessObjects;

public sealed record Sighting(
    Guid Id,
    string? Name,
    string Description,
    string Behaviour,
    double Latitude,
    double Longitude,
    SightingStatus Status,
    SightingCriticality Criticality,
    string ReporterDisplayName,
    DateTime CreatedAt);

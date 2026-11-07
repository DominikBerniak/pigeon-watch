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
    Guid ReporterId,
    string ReporterDisplayName,
    DateTime CreatedAt,
    bool HasPhoto = false);

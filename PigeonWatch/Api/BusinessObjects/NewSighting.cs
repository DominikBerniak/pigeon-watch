namespace PigeonWatch.BusinessObjects;

public sealed record NewSighting(
    Guid ReporterId,
    string? Name,
    string Description,
    string Behaviour,
    double Latitude,
    double Longitude,
    SightingStatus Status,
    SightingCriticality Criticality);

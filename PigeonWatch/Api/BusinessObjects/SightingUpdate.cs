namespace PigeonWatch.BusinessObjects;

public sealed record SightingUpdate(
    Guid SightingId,
    Guid UserId,
    string? Name,
    string Description,
    string Behaviour,
    SightingStatus Status,
    SightingCriticality Criticality,
    NewSightingPhoto? Photo = null,
    bool RemovePhoto = false);

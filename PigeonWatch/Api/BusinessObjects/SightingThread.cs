namespace PigeonWatch.BusinessObjects;

public sealed record SightingThread(
    Guid Id,
    Guid SightingId,
    string? SightingName,
    Guid OwnerId,
    string OwnerDisplayName,
    Guid ParticipantId,
    string ParticipantDisplayName,
    DateTime LastMessageAt);

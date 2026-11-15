namespace PigeonWatch.BusinessObjects;

public sealed record SightingUpdateOutcome(Sighting Sighting, string? ReplacedPhotoBlobName);

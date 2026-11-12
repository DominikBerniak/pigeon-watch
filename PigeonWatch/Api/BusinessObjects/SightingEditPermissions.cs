namespace PigeonWatch.BusinessObjects;

public sealed record SightingEditPermissions(bool CanEditDescription, IReadOnlyList<SightingStatus> AllowedNextStatuses);

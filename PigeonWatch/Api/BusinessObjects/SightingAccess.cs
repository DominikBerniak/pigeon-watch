namespace PigeonWatch.BusinessObjects;

public sealed record SightingAccess(Guid ReporterId, DateTime? RemovedAt);

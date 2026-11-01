namespace PigeonWatch.BusinessObjects;

public sealed record NearbySightingFilters(
    IReadOnlyCollection<SightingStatus> Statuses,
    IReadOnlyCollection<SightingCriticality> Criticalities);

namespace PigeonWatch.BusinessObjects;

public sealed record SightingListQuery(
    double Latitude,
    double Longitude,
    IReadOnlyCollection<SightingStatus> Statuses,
    IReadOnlyCollection<SightingCriticality> Criticalities,
    int Page,
    int PageSize);

namespace PigeonWatch.BusinessObjects;

public sealed record SightingPage(IReadOnlyList<Sighting> Items, int TotalCount);

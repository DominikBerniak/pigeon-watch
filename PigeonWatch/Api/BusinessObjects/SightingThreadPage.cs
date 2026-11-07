namespace PigeonWatch.BusinessObjects;

public sealed record SightingThreadPage(IReadOnlyList<SightingThread> Items, int TotalCount);

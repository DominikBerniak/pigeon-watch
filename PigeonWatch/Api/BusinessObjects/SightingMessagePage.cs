namespace PigeonWatch.BusinessObjects;

public sealed record SightingMessagePage(IReadOnlyList<SightingMessage> Items, int TotalCount);

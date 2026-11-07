namespace PigeonWatch.BusinessObjects;

public sealed record SightingCommentPage(IReadOnlyList<SightingComment> Items, int TotalCount);

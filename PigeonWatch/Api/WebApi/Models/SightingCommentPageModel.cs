namespace PigeonWatch.WebApi.Models;

public sealed class SightingCommentPageModel
{
    public required IReadOnlyList<SightingCommentModel> Items { get; init; }

    public required int TotalCount { get; init; }
}

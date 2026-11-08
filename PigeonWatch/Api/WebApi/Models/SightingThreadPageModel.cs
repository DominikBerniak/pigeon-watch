namespace PigeonWatch.WebApi.Models;

public sealed class SightingThreadPageModel
{
    public required IReadOnlyList<SightingThreadModel> Items { get; init; }

    public required int TotalCount { get; init; }
}

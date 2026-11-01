namespace PigeonWatch.WebApi.Models;

public sealed class SightingPageModel
{
    public required IReadOnlyList<SightingModel> Items { get; init; }

    public required int TotalCount { get; init; }
}

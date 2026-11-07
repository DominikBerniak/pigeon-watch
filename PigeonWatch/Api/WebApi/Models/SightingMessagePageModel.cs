namespace PigeonWatch.WebApi.Models;

public sealed class SightingMessagePageModel
{
    public required IReadOnlyList<SightingMessageModel> Items { get; init; }

    public required int TotalCount { get; init; }
}

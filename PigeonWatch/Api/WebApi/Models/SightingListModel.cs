namespace PigeonWatch.WebApi.Models;

public sealed class SightingListModel
{
    public required IReadOnlyList<SightingModel> Items { get; init; }
}

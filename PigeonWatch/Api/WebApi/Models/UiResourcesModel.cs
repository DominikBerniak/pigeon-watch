namespace PigeonWatch.WebApi.Models;

public sealed class UiResourcesModel
{
    public required string Culture { get; init; }

    public required IReadOnlyDictionary<string, string> Labels { get; init; }
}

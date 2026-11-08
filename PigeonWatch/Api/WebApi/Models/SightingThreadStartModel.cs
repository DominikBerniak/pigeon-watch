namespace PigeonWatch.WebApi.Models;

public sealed class SightingThreadStartModel
{
    public required SightingThreadModel Thread { get; init; }

    public required SightingMessageModel Message { get; init; }
}

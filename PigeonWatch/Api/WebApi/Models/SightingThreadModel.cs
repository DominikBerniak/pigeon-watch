namespace PigeonWatch.WebApi.Models;

public sealed class SightingThreadModel
{
    public required Guid Id { get; init; }

    public required Guid SightingId { get; init; }

    public required string? SightingName { get; init; }

    public required string CounterpartDisplayName { get; init; }

    public required bool IsOwner { get; init; }

    public required DateTime LastMessageAt { get; init; }

    public required bool HasUnread { get; init; }
}

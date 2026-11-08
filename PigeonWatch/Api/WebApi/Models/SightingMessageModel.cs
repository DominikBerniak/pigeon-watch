namespace PigeonWatch.WebApi.Models;

public sealed class SightingMessageModel
{
    public required Guid Id { get; init; }

    public required string Text { get; init; }

    public required string SenderDisplayName { get; init; }

    public required DateTime CreatedAt { get; init; }

    public required bool IsMine { get; init; }
}

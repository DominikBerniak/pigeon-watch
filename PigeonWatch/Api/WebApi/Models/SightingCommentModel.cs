namespace PigeonWatch.WebApi.Models;

public sealed class SightingCommentModel
{
    public required Guid Id { get; init; }

    public required string? Text { get; init; }

    public required string? AuthorDisplayName { get; init; }

    public required DateTime CreatedAt { get; init; }

    public required DateTime? EditedAt { get; init; }

    public required bool IsDeleted { get; init; }

    public required bool IsMine { get; init; }
}

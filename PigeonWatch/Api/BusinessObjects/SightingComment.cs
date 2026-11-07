namespace PigeonWatch.BusinessObjects;

public sealed record SightingComment(
    Guid Id,
    Guid SightingId,
    Guid AuthorId,
    string AuthorDisplayName,
    string Text,
    DateTime CreatedAt,
    DateTime? EditedAt,
    DateTime? DeletedAt);

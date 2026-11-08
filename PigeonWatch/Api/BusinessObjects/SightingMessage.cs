namespace PigeonWatch.BusinessObjects;

public sealed record SightingMessage(
    Guid Id,
    Guid ThreadId,
    Guid SenderId,
    string SenderDisplayName,
    string Text,
    DateTime CreatedAt);

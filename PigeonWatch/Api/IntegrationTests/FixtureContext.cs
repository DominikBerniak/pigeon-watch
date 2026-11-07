namespace PigeonWatch.IntegrationTests;

public sealed record FixtureContext(
    IReadOnlyList<string> RegisteredEmails,
    IReadOnlyList<string> RegisteredDisplayNames,
    Guid SightingId,
    Guid CommentId,
    Guid ThreadId);

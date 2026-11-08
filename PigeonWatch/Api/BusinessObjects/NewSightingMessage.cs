namespace PigeonWatch.BusinessObjects;

public sealed record NewSightingMessage(Guid ThreadId, Guid SenderId, string Text);

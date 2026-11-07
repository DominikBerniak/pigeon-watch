namespace PigeonWatch.BusinessObjects;

public sealed record NewSightingComment(Guid SightingId, Guid AuthorId, string Text);

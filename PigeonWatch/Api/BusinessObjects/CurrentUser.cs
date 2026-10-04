namespace PigeonWatch.BusinessObjects;

public sealed record CurrentUser(Guid Id, string Email, string DisplayName);

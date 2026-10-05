namespace PigeonWatch.WebApi.Models;

public sealed class CurrentUserModel
{
    public required Guid Id { get; init; }

    public required string Email { get; init; }

    public required string DisplayName { get; init; }

    public required IReadOnlyList<string> Roles { get; init; }
}

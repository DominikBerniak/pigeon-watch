namespace PigeonWatch.WebApi.Models;

public sealed class RegisteredAccountModel
{
    public required string Email { get; init; }

    public required string DisplayName { get; init; }
}

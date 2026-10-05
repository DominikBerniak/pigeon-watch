namespace PigeonWatch.WebApi.Models;

public sealed class RegisterRequestModel
{
    public required string Email { get; init; }

    public required string Password { get; init; }

    public required string DisplayName { get; init; }
}

namespace PigeonWatch.WebApi.Models;

public sealed class UpdatedProfileModel
{
    public required string Email { get; init; }
    public required string DisplayName { get; init; }
}

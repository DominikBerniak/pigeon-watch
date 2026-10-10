namespace PigeonWatch.WebApi.Models;

public sealed class ChangePasswordRequestModel
{
    public required string CurrentPassword { get; init; }
    public required string NewPassword { get; init; }
}

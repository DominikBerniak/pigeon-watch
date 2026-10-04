namespace PigeonWatch.WebApi.Models;

public sealed class PasswordRulesModel
{
    public required int MinLength { get; init; }

    public required bool RequireDigit { get; init; }

    public required bool RequireLowercase { get; init; }

    public required bool RequireUppercase { get; init; }

    public required bool RequireNonAlphanumeric { get; init; }
}

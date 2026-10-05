namespace PigeonWatch.WebApi.Models;

public sealed class ClientConfigurationModel
{
    public required PasswordRulesModel PasswordRules { get; init; }

    public required DisplayNameRulesModel DisplayNameRules { get; init; }
}

namespace PigeonWatch.WebApi.Models;

public sealed class CommentRulesModel
{
    public required int TextMaxLength { get; init; }

    public required int CooldownSeconds { get; init; }

    public required int PageSize { get; init; }
}

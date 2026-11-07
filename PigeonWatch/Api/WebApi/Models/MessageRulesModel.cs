namespace PigeonWatch.WebApi.Models;

public sealed class MessageRulesModel
{
    public required int TextMaxLength { get; init; }

    public required int PageSize { get; init; }
}

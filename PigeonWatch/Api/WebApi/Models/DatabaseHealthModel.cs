using System.Text.Json.Serialization;

namespace PigeonWatch.WebApi.Models;

public sealed class DatabaseHealthModel
{
    public required string Status { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? Id { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? CreatedAtUtc { get; init; }
}

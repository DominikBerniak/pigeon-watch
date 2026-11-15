namespace PigeonWatch.WebApi.Models;

public sealed class SightingEditPermissionsModel
{
    public required bool CanEditDescription { get; init; }

    public required IReadOnlyList<int> AllowedNextStatuses { get; init; }
}

using PigeonWatch.BusinessObjects;

namespace PigeonWatch.BusinessLogic.Services;

public interface ISightingService
{
    Task<SightingCreateResult> CreateAsync(NewSighting sighting, CancellationToken cancellationToken = default);
    Task<Sighting?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Sighting>> GetNearbyAsync(double latitude, double longitude, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Sighting>> GetMineAsync(Guid reporterId, CancellationToken cancellationToken = default);
}

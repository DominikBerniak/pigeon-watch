using PigeonWatch.BusinessObjects;

namespace PigeonWatch.BusinessLogic.Services;

public interface ISightingService
{
    Task<SightingCreateResult> CreateAsync(NewSighting sighting, CancellationToken cancellationToken = default);
    Task<Sighting?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<SightingNearbyResult> GetNearbyAsync(
        double latitude,
        double longitude,
        IReadOnlyCollection<SightingStatus> statuses,
        IReadOnlyCollection<SightingCriticality> criticalities,
        CancellationToken cancellationToken = default);
    Task<SightingListResult> GetListPageAsync(SightingListQuery query, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Sighting>> GetMineAsync(Guid reporterId, CancellationToken cancellationToken = default);
}

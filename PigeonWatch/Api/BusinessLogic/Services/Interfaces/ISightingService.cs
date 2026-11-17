using PigeonWatch.BusinessObjects;

namespace PigeonWatch.BusinessLogic.Services;

public interface ISightingService
{
    Task<SightingCreateResult> CreateAsync(NewSighting sighting, CancellationToken cancellationToken = default);
    Task<SightingUpdateResult> UpdateAsync(SightingUpdate update, bool isAdmin, CancellationToken cancellationToken = default);
    Task<SightingRemovalResult> RemoveAsync(Guid sightingId, Guid userId, bool isAdmin, CancellationToken cancellationToken = default);
    Task<SightingRemovalResult> RestoreAsync(Guid sightingId, bool isAdmin, CancellationToken cancellationToken = default);
    Task<Sighting?> GetAsync(Guid id, Guid userId, bool isAdmin, CancellationToken cancellationToken = default);
    Task<SightingEditPermissions?> GetEditPermissionsAsync(Sighting sighting, Guid userId, CancellationToken cancellationToken = default);
    Task<SightingNearbyResult> GetNearbyAsync(
        double latitude,
        double longitude,
        IReadOnlyCollection<SightingStatus> statuses,
        IReadOnlyCollection<SightingCriticality> criticalities,
        bool isAdmin,
        CancellationToken cancellationToken = default);
    Task<SightingListResult> GetListPageAsync(SightingListQuery query, bool isAdmin, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Sighting>> GetMineAsync(Guid reporterId, CancellationToken cancellationToken = default);
    Task<Stream?> OpenPhotoAsync(Guid sightingId, Guid userId, bool isAdmin, CancellationToken cancellationToken = default);
    Task<Stream?> OpenPhotoThumbnailAsync(Guid sightingId, Guid userId, bool isAdmin, CancellationToken cancellationToken = default);
}

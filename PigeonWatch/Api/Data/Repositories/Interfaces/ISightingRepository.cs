using PigeonWatch.BusinessObjects;

namespace PigeonWatch.Data.Repositories;

public interface ISightingRepository
{
    Task<Sighting?> CreateAsync(NewSighting sighting, CancellationToken cancellationToken = default);
    Task<Sighting?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Sighting>> ListInBoxAsync(double minLatitude, double maxLatitude, double minLongitude, double maxLongitude, IReadOnlyCollection<SightingStatus> statuses, IReadOnlyCollection<SightingCriticality> criticalities, int take, bool includeRemoved, CancellationToken cancellationToken = default);
    Task<SightingPage> ListPageInRadiusAsync(GeoRadiusFilter radius, IReadOnlyCollection<SightingStatus> statuses, IReadOnlyCollection<SightingCriticality> criticalities, int skip, int take, bool includeRemoved, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Sighting>> ListByReporterAsync(Guid reporterId, int take, CancellationToken cancellationToken = default);
    Task<DateTime?> GetLatestCreateDateByReporterAsync(Guid reporterId, CancellationToken cancellationToken = default);
    Task<SightingAccess?> GetAccessAsync(Guid sightingId, CancellationToken cancellationToken = default);
    Task<string?> GetPhotoBlobNameAsync(Guid sightingId, CancellationToken cancellationToken = default);
    Task<bool> HasOtherUserEngagementAsync(Guid sightingId, Guid reporterId, CancellationToken cancellationToken = default);
    Task<SightingUpdateOutcome?> UpdateAsync(Guid sightingId, Guid reporterId, SightingUpdate update, SightingPhotoChange photoChange, string? newPhotoBlobName, CancellationToken cancellationToken = default);
    Task<Sighting?> RemoveAsync(Guid sightingId, Guid removedBy, DateTime removedAt, CancellationToken cancellationToken = default);
    Task<Sighting?> RestoreAsync(Guid sightingId, CancellationToken cancellationToken = default);
}

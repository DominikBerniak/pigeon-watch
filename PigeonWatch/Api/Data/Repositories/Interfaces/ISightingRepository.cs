using PigeonWatch.BusinessObjects;

namespace PigeonWatch.Data.Repositories;

public interface ISightingRepository
{
    Task<Sighting?> CreateAsync(NewSighting sighting, CancellationToken cancellationToken = default);
    Task<Sighting?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Sighting>> ListInBoxAsync(double minLatitude, double maxLatitude, double minLongitude, double maxLongitude, IReadOnlyCollection<SightingStatus> statuses, int take, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Sighting>> ListByReporterAsync(Guid reporterId, int take, CancellationToken cancellationToken = default);
    Task<DateTime?> GetLatestCreateDateByReporterAsync(Guid reporterId, CancellationToken cancellationToken = default);
}

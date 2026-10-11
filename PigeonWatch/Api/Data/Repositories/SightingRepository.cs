using Microsoft.EntityFrameworkCore;
using PigeonWatch.BusinessObjects;
using PigeonWatch.Data.Entities;
using PigeonWatch.Data.Mappers;

namespace PigeonWatch.Data.Repositories;

public class SightingRepository(PigeonWatchDbContext db, ISightingMapper sightingMapper) : ISightingRepository
{
    public async Task<Sighting?> CreateAsync(NewSighting sighting, CancellationToken cancellationToken = default)
    {
        UserAccountEntity? reporter = await db.UserAccounts.FindAsync([sighting.ReporterId], cancellationToken);

        if (reporter is null)
            return null;

        SightingEntity entity = sightingMapper.ToEntity(sighting);
        entity.Reporter = reporter;
        db.Sightings.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        return sightingMapper.ToSighting(entity);
    }

    public async Task<Sighting?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        SightingEntity? entity = await db.Sightings
            .AsNoTracking()
            .Include(e => e.Reporter)
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

        return entity is null ? null : sightingMapper.ToSighting(entity);
    }

    public async Task<IReadOnlyList<Sighting>> ListInBoxAsync(
        double minLatitude,
        double maxLatitude,
        double minLongitude,
        double maxLongitude,
        IReadOnlyCollection<SightingStatus> statuses,
        CancellationToken cancellationToken = default)
    {
        List<SightingStatus> statusList = statuses.ToList();
        List<SightingEntity> entities = await db.Sightings
            .AsNoTracking()
            .Include(e => e.Reporter)
            .Where(e => e.Latitude >= minLatitude
                && e.Latitude <= maxLatitude
                && e.Longitude >= minLongitude
                && e.Longitude <= maxLongitude
                && statusList.Contains(e.Status))
            .OrderByDescending(e => e.CreateDate)
            .ToListAsync(cancellationToken);

        return entities.Select(sightingMapper.ToSighting).ToList();
    }

    public async Task<IReadOnlyList<Sighting>> ListByReporterAsync(Guid reporterId, int take, CancellationToken cancellationToken = default)
    {
        List<SightingEntity> entities = await db.Sightings
            .AsNoTracking()
            .Include(e => e.Reporter)
            .Where(e => e.ReporterId == reporterId)
            .OrderByDescending(e => e.CreateDate)
            .Take(take)
            .ToListAsync(cancellationToken);

        return entities.Select(sightingMapper.ToSighting).ToList();
    }

    public async Task<DateTime?> GetLatestCreateDateByReporterAsync(Guid reporterId, CancellationToken cancellationToken = default)
    {
        DateTime? latest = await db.Sightings
            .Where(e => e.ReporterId == reporterId)
            .OrderByDescending(e => e.CreateDate)
            .Select(e => (DateTime?)e.CreateDate)
            .FirstOrDefaultAsync(cancellationToken);

        return latest is null ? null : DateTime.SpecifyKind(latest.Value, DateTimeKind.Utc);
    }
}

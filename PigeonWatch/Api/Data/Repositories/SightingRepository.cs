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
        IReadOnlyCollection<SightingCriticality> criticalities,
        int take,
        CancellationToken cancellationToken = default)
    {
        List<SightingStatus> statusList = statuses.ToList();
        IQueryable<SightingEntity> query = db.Sightings
            .AsNoTracking()
            .Include(e => e.Reporter)
            .Where(e => e.Latitude >= minLatitude
                && e.Latitude <= maxLatitude
                && e.Longitude >= minLongitude
                && e.Longitude <= maxLongitude
                && statusList.Contains(e.Status));

        if (criticalities.Count > 0)
        {
            List<SightingCriticality> criticalityList = criticalities.ToList();
            query = query.Where(e => criticalityList.Contains(e.Criticality));
        }

        List<SightingEntity> entities = await query
            .OrderByDescending(e => e.CreateDate)
            .Take(take)
            .ToListAsync(cancellationToken);

        return entities.Select(sightingMapper.ToSighting).ToList();
    }

    public async Task<SightingPage> ListPageInRadiusAsync(
        GeoRadiusFilter radius,
        IReadOnlyCollection<SightingStatus> statuses,
        IReadOnlyCollection<SightingCriticality> criticalities,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        const double radiansPerDegree = Math.PI / 180;
        double minLatitude = radius.Box.MinLatitude;
        double maxLatitude = radius.Box.MaxLatitude;
        double minLongitude = radius.Box.MinLongitude;
        double maxLongitude = radius.Box.MaxLongitude;
        double centreLatitude = radius.CentreLatitudeRadians;
        double centreLongitude = radius.CentreLongitudeRadians;
        double cosCentreLatitude = radius.CosCentreLatitude;
        double halfChordThreshold = radius.HalfChordThreshold;
        List<SightingStatus> statusList = statuses.ToList();
        IQueryable<SightingEntity> query = db.Sightings
            .AsNoTracking()
            .Where(e => e.Latitude >= minLatitude
                && e.Latitude <= maxLatitude
                && e.Longitude >= minLongitude
                && e.Longitude <= maxLongitude
                && statusList.Contains(e.Status))
            .Where(e =>
                Math.Sin((e.Latitude * radiansPerDegree - centreLatitude) / 2) * Math.Sin((e.Latitude * radiansPerDegree - centreLatitude) / 2)
                + cosCentreLatitude * Math.Cos(e.Latitude * radiansPerDegree)
                    * Math.Sin((e.Longitude * radiansPerDegree - centreLongitude) / 2) * Math.Sin((e.Longitude * radiansPerDegree - centreLongitude) / 2)
                <= halfChordThreshold);

        if (criticalities.Count > 0)
        {
            List<SightingCriticality> criticalityList = criticalities.ToList();
            query = query.Where(e => criticalityList.Contains(e.Criticality));
        }

        int totalCount = await query.CountAsync(cancellationToken);
        List<SightingEntity> entities = await query
            .OrderByDescending(e => e.Criticality)
            .ThenBy(e => e.CreateDate)
            .ThenBy(e => e.Id)
            .Skip(skip)
            .Take(take)
            .Include(e => e.Reporter)
            .ToListAsync(cancellationToken);

        return new SightingPage(entities.Select(sightingMapper.ToSighting).ToList(), totalCount);
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

    public async Task<string?> GetPhotoBlobNameAsync(Guid sightingId, CancellationToken cancellationToken = default)
    {
        string? blobName = await db.Sightings
            .Where(e => e.Id == sightingId)
            .Select(e => e.PhotoBlobName)
            .FirstOrDefaultAsync(cancellationToken);

        return blobName;
    }
}

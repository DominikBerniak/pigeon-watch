using PigeonWatch.BusinessObjects;
using PigeonWatch.Data.Entities;

namespace PigeonWatch.Data.Mappers;

public class SightingMapper : ISightingMapper
{
    SightingEntity ISightingMapper.ToEntity(NewSighting sighting)
    {
        return new()
        {
            ReporterId = sighting.ReporterId,
            Name = sighting.Name,
            Description = sighting.Description,
            Behaviour = sighting.Behaviour,
            Latitude = sighting.Latitude,
            Longitude = sighting.Longitude,
            Status = sighting.Status,
            Criticality = sighting.Criticality
        };
    }

    Sighting ISightingMapper.ToSighting(SightingEntity entity)
    {
        return new Sighting(
            entity.Id,
            entity.Name,
            entity.Description,
            entity.Behaviour,
            entity.Latitude,
            entity.Longitude,
            entity.Status,
            entity.Criticality,
            entity.Reporter.DisplayName,
            DateTime.SpecifyKind(entity.CreateDate, DateTimeKind.Utc));
    }
}

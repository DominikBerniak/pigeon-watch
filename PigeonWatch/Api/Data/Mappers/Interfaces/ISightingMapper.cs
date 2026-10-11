using PigeonWatch.BusinessObjects;
using PigeonWatch.Data.Entities;

namespace PigeonWatch.Data.Mappers;

public interface ISightingMapper
{
    internal SightingEntity ToEntity(NewSighting sighting);
    internal Sighting ToSighting(SightingEntity entity);
}

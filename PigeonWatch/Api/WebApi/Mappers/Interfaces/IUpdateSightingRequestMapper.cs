using PigeonWatch.BusinessObjects;
using PigeonWatch.WebApi.Models;

namespace PigeonWatch.WebApi.Mappers;

public interface IUpdateSightingRequestMapper
{
    SightingUpdate Map(Guid sightingId, Guid userId, UpdateSightingRequestModel request);
}

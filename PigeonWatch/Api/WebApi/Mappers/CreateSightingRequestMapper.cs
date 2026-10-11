using PigeonWatch.BusinessObjects;
using PigeonWatch.WebApi.Models;

namespace PigeonWatch.WebApi.Mappers;

public class CreateSightingRequestMapper : ICreateSightingRequestMapper
{
    public NewSighting Map(Guid reporterId, CreateSightingRequestModel request)
    {
        return new(
            reporterId,
            request.Name,
            request.Description,
            request.Behaviour,
            request.Latitude,
            request.Longitude,
            (SightingStatus)request.Status,
            (SightingCriticality)request.Criticality);
    }
}

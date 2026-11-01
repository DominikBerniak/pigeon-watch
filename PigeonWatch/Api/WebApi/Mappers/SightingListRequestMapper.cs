using PigeonWatch.BusinessObjects;
using PigeonWatch.WebApi.Models;

namespace PigeonWatch.WebApi.Mappers;

public class SightingListRequestMapper : ISightingListRequestMapper
{
    public SightingListQuery Map(SightingListRequestModel request)
    {
        return new(
            request.Latitude,
            request.Longitude,
            request.Statuses.Select(status => (SightingStatus)status).ToList(),
            request.Criticalities.Select(criticality => (SightingCriticality)criticality).ToList(),
            request.Page,
            request.PageSize);
    }
}

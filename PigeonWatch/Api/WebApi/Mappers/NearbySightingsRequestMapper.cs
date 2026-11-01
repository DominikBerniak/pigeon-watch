using PigeonWatch.BusinessObjects;
using PigeonWatch.WebApi.Models;

namespace PigeonWatch.WebApi.Mappers;

public class NearbySightingsRequestMapper : INearbySightingsRequestMapper
{
    public NearbySightingFilters MapFilters(NearbySightingsRequestModel request)
    {
        return new(
            request.Statuses.Select(status => (SightingStatus)status).ToList(),
            request.Criticalities.Select(criticality => (SightingCriticality)criticality).ToList());
    }
}

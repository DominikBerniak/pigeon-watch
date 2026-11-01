using PigeonWatch.BusinessObjects;
using PigeonWatch.WebApi.Models;

namespace PigeonWatch.WebApi.Mappers;

public interface INearbySightingsRequestMapper
{
    NearbySightingFilters MapFilters(NearbySightingsRequestModel request);
}

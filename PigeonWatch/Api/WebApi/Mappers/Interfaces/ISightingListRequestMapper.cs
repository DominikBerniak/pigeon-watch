using PigeonWatch.BusinessObjects;
using PigeonWatch.WebApi.Models;

namespace PigeonWatch.WebApi.Mappers;

public interface ISightingListRequestMapper
{
    SightingListQuery Map(SightingListRequestModel request);
}

using PigeonWatch.BusinessObjects;
using PigeonWatch.WebApi.Models;

namespace PigeonWatch.WebApi.Mappers;

public interface ICreateSightingRequestMapper
{
    NewSighting Map(Guid reporterId, CreateSightingRequestModel request);
}

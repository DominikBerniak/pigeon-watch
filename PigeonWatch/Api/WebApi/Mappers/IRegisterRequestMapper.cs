using PigeonWatch.BusinessObjects;
using PigeonWatch.WebApi.Models;

namespace PigeonWatch.WebApi.Mappers;

public interface IRegisterRequestMapper
{
    NewAccount Map(RegisterRequestModel request);
}

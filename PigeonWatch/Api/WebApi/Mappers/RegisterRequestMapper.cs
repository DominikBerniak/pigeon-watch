using PigeonWatch.BusinessObjects;
using PigeonWatch.WebApi.Models;

namespace PigeonWatch.WebApi.Mappers;

public class RegisterRequestMapper : IRegisterRequestMapper
{
    public NewAccount Map(RegisterRequestModel request)
    {
        return new(request.Email, request.Password, request.DisplayName);
    }
}

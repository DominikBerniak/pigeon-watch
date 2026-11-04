using Microsoft.AspNetCore.Http;
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
            (SightingCriticality)request.Criticality,
            MapPhoto(request.Photo));
    }

    private static NewSightingPhoto? MapPhoto(IFormFile? photo)
    {
        if (photo is null || photo.Length == 0)
            return null;

        return new NewSightingPhoto(photo.OpenReadStream(), photo.Length);
    }
}

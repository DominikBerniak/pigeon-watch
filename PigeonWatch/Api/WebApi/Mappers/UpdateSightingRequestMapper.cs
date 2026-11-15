using Microsoft.AspNetCore.Http;
using PigeonWatch.BusinessObjects;
using PigeonWatch.WebApi.Models;

namespace PigeonWatch.WebApi.Mappers;

public class UpdateSightingRequestMapper : IUpdateSightingRequestMapper
{
    public SightingUpdate Map(Guid sightingId, Guid userId, UpdateSightingRequestModel request)
    {
        return new(
            sightingId,
            userId,
            request.Name,
            request.Description,
            request.Behaviour,
            (SightingStatus)request.Status,
            (SightingCriticality)request.Criticality,
            MapPhoto(request.Photo),
            request.RemovePhoto);
    }

    private static NewSightingPhoto? MapPhoto(IFormFile? photo)
    {
        if (photo is null || photo.Length == 0)
            return null;

        return new NewSightingPhoto(photo.OpenReadStream(), photo.Length);
    }
}

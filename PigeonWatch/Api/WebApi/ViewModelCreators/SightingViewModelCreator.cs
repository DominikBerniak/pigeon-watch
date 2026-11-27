using PigeonWatch.BusinessObjects;
using PigeonWatch.WebApi.Models;

namespace PigeonWatch.WebApi.ViewModelCreators;

public class SightingViewModelCreator : ISightingViewModelCreator
{
    public SightingModel Create(Sighting sighting, Guid currentUserId, SightingEditPermissions? permissions)
    {
        return new()
        {
            Id = sighting.Id,
            Name = sighting.Name,
            Description = sighting.Description,
            Behaviour = sighting.Behaviour,
            Latitude = sighting.Latitude,
            Longitude = sighting.Longitude,
            Status = (int)sighting.Status,
            Criticality = (int)sighting.Criticality,
            ReporterDisplayName = sighting.ReporterDisplayName,
            CreatedAt = sighting.CreatedAt,
            HasPhoto = sighting.HasPhoto,
            RemovedAt = sighting.RemovedAt,
            IsMine = sighting.ReporterId == currentUserId,
            EditPermissions = permissions is null
                ? null
                : new SightingEditPermissionsModel
                {
                    CanEditDescription = permissions.CanEditDescription,
                    AllowedNextStatuses = permissions.AllowedNextStatuses.Select(status => (int)status).ToList()
                }
        };
    }

    public SightingListModel CreateList(IReadOnlyList<Sighting> sightings, Guid currentUserId)
    {
        return new()
        {
            Items = sightings.Select(sighting => Create(sighting, currentUserId, null)).ToList()
        };
    }

    public SightingPageModel CreatePage(SightingPage page, Guid currentUserId)
    {
        return new()
        {
            Items = page.Items.Select(sighting => Create(sighting, currentUserId, null)).ToList(),
            TotalCount = page.TotalCount
        };
    }
}

using PigeonWatch.BusinessObjects;
using PigeonWatch.WebApi.Models;

namespace PigeonWatch.WebApi.ViewModelCreators;

public class SightingViewModelCreator : ISightingViewModelCreator
{
    public SightingModel Create(Sighting sighting, Guid currentUserId)
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
            IsMine = sighting.ReporterId == currentUserId
        };
    }

    public SightingListModel CreateList(IReadOnlyList<Sighting> sightings, Guid currentUserId)
    {
        return new()
        {
            Items = sightings.Select(sighting => Create(sighting, currentUserId)).ToList()
        };
    }

    public SightingPageModel CreatePage(SightingPage page, Guid currentUserId)
    {
        return new()
        {
            Items = page.Items.Select(sighting => Create(sighting, currentUserId)).ToList(),
            TotalCount = page.TotalCount
        };
    }
}

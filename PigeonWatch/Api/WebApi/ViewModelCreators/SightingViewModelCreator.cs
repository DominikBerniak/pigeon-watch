using PigeonWatch.BusinessObjects;
using PigeonWatch.WebApi.Models;

namespace PigeonWatch.WebApi.ViewModelCreators;

public class SightingViewModelCreator : ISightingViewModelCreator
{
    public SightingModel Create(Sighting sighting)
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
            CreatedAt = sighting.CreatedAt
        };
    }

    public SightingListModel CreateList(IReadOnlyList<Sighting> sightings)
    {
        return new()
        {
            Items = sightings.Select(Create).ToList()
        };
    }
}

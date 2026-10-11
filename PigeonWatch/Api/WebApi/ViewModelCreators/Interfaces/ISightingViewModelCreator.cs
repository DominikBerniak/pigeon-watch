using PigeonWatch.BusinessObjects;
using PigeonWatch.WebApi.Models;

namespace PigeonWatch.WebApi.ViewModelCreators;

public interface ISightingViewModelCreator
{
    SightingModel Create(Sighting sighting);
    SightingListModel CreateList(IReadOnlyList<Sighting> sightings);
}

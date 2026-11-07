using PigeonWatch.BusinessObjects;
using PigeonWatch.WebApi.Models;

namespace PigeonWatch.WebApi.ViewModelCreators;

public interface ISightingViewModelCreator
{
    SightingModel Create(Sighting sighting, Guid currentUserId);
    SightingListModel CreateList(IReadOnlyList<Sighting> sightings, Guid currentUserId);
    SightingPageModel CreatePage(SightingPage page, Guid currentUserId);
}

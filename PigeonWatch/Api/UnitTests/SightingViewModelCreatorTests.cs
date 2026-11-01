using PigeonWatch.BusinessObjects;
using PigeonWatch.WebApi.Models;
using PigeonWatch.WebApi.ViewModelCreators;

namespace PigeonWatch.UnitTests;

public class SightingViewModelCreatorTests
{
    [Fact]
    public void CreatePage_maps_every_item_in_order_and_keeps_the_total_count()
    {
        Sighting urgent = Sighting(SightingStatus.Caught, SightingCriticality.Urgent);
        Sighting monitor = Sighting(SightingStatus.Adopted, SightingCriticality.Monitor);

        SightingPageModel model = new SightingViewModelCreator().CreatePage(new SightingPage([urgent, monitor], 25));

        Assert.Equal([urgent.Id, monitor.Id], model.Items.Select(item => item.Id));
        Assert.Equal([2, 6], model.Items.Select(item => item.Status));
        Assert.Equal([3, 1], model.Items.Select(item => item.Criticality));
        Assert.Equal(25, model.TotalCount);
    }

    [Fact]
    public void CreatePage_keeps_the_total_count_for_an_empty_page_past_the_end()
    {
        SightingPageModel model = new SightingViewModelCreator().CreatePage(new SightingPage([], 12));

        Assert.Empty(model.Items);
        Assert.Equal(12, model.TotalCount);
    }

    private static Sighting Sighting(SightingStatus status, SightingCriticality criticality)
    {
        return new(
            Guid.NewGuid(),
            "Grey",
            "Limping near the fountain",
            "Cannot fly",
            52.2297,
            21.0122,
            status,
            criticality,
            "Pidgey",
            new DateTime(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc));
    }
}

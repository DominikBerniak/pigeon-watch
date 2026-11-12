using PigeonWatch.BusinessObjects;
using PigeonWatch.WebApi.Models;
using PigeonWatch.WebApi.ViewModelCreators;

namespace PigeonWatch.UnitTests;

public class SightingViewModelCreatorTests
{
    private readonly Guid reporterId = Guid.NewGuid();

    [Fact]
    public void CreatePage_maps_every_item_in_order_and_keeps_the_total_count()
    {
        Sighting urgent = Sighting(SightingStatus.Caught, SightingCriticality.Urgent);
        Sighting monitor = Sighting(SightingStatus.Adopted, SightingCriticality.Monitor);

        SightingPageModel model = new SightingViewModelCreator().CreatePage(new SightingPage([urgent, monitor], 25), Guid.NewGuid());

        Assert.Equal([urgent.Id, monitor.Id], model.Items.Select(item => item.Id));
        Assert.Equal([2, 6], model.Items.Select(item => item.Status));
        Assert.Equal([3, 1], model.Items.Select(item => item.Criticality));
        Assert.Equal(25, model.TotalCount);
    }

    [Fact]
    public void CreatePage_keeps_the_total_count_for_an_empty_page_past_the_end()
    {
        SightingPageModel model = new SightingViewModelCreator().CreatePage(new SightingPage([], 12), Guid.NewGuid());

        Assert.Empty(model.Items);
        Assert.Equal(12, model.TotalCount);
    }

    [Fact]
    public void Create_marks_the_sighting_as_mine_only_for_its_reporter()
    {
        Sighting sighting = Sighting(SightingStatus.Spotted, SightingCriticality.Urgent);
        SightingViewModelCreator creator = new();

        SightingModel mine = creator.Create(sighting, reporterId, null);
        SightingModel other = creator.Create(sighting, Guid.NewGuid(), null);

        Assert.True(mine.IsMine);
        Assert.False(other.IsMine);
    }

    [Fact]
    public void Create_maps_the_edit_permissions_to_integers_and_leaves_them_null_without_permissions()
    {
        Sighting sighting = Sighting(SightingStatus.Spotted, SightingCriticality.Urgent);
        SightingViewModelCreator creator = new();

        SightingModel withPermissions = creator.Create(sighting, reporterId, new SightingEditPermissions(false, [SightingStatus.Caught, SightingStatus.NotFound]));
        SightingModel withoutPermissions = creator.Create(sighting, reporterId, null);

        Assert.NotNull(withPermissions.EditPermissions);
        Assert.False(withPermissions.EditPermissions.CanEditDescription);
        Assert.Equal([2, 9], withPermissions.EditPermissions.AllowedNextStatuses);
        Assert.Null(withoutPermissions.EditPermissions);
    }

    [Fact]
    public void CreateList_and_CreatePage_leave_the_edit_permissions_null_even_for_the_owner()
    {
        Sighting mine = Sighting(SightingStatus.Spotted, SightingCriticality.Urgent);
        SightingViewModelCreator creator = new();

        SightingListModel list = creator.CreateList([mine], reporterId);
        SightingPageModel page = creator.CreatePage(new SightingPage([mine], 1), reporterId);

        Assert.All(list.Items, item => Assert.Null(item.EditPermissions));
        Assert.All(page.Items, item => Assert.Null(item.EditPermissions));
    }

    [Fact]
    public void CreateList_and_CreatePage_compute_is_mine_per_item_against_the_caller()
    {
        Sighting mine = Sighting(SightingStatus.Spotted, SightingCriticality.Urgent);
        Sighting other = Sighting(SightingStatus.Spotted, SightingCriticality.Urgent) with { ReporterId = Guid.NewGuid() };
        SightingViewModelCreator creator = new();

        SightingListModel list = creator.CreateList([mine, other], reporterId);
        SightingPageModel page = creator.CreatePage(new SightingPage([mine, other], 2), reporterId);

        Assert.Equal([true, false], list.Items.Select(item => item.IsMine));
        Assert.Equal([true, false], page.Items.Select(item => item.IsMine));
    }

    private Sighting Sighting(SightingStatus status, SightingCriticality criticality)
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
            reporterId,
            "Pidgey",
            new DateTime(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc));
    }
}

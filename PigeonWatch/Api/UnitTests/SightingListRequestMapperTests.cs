using PigeonWatch.BusinessObjects;
using PigeonWatch.WebApi.Mappers;
using PigeonWatch.WebApi.Models;

namespace PigeonWatch.UnitTests;

public class SightingListRequestMapperTests
{
    [Fact]
    public void Map_casts_the_filter_values_to_enums_and_keeps_the_centre_and_paging()
    {
        SightingListRequestModel request = new()
        {
            Latitude = 52.1,
            Longitude = 21.1,
            Statuses = [1, 6],
            Criticalities = [3, 1],
            Page = 2,
            PageSize = 20
        };

        SightingListQuery query = new SightingListRequestMapper().Map(request);

        Assert.Equal(52.1, query.Latitude);
        Assert.Equal(21.1, query.Longitude);
        Assert.Equal([SightingStatus.Spotted, SightingStatus.Adopted], query.Statuses);
        Assert.Equal([SightingCriticality.Urgent, SightingCriticality.Monitor], query.Criticalities);
        Assert.Equal(2, query.Page);
        Assert.Equal(20, query.PageSize);
    }

    [Fact]
    public void Map_keeps_undefined_values_for_the_service_to_reject()
    {
        SightingListRequestModel request = new() { Latitude = 52.1, Longitude = 21.1, Statuses = [42], Criticalities = [9], Page = 0, PageSize = 15 };

        SightingListQuery query = new SightingListRequestMapper().Map(request);

        Assert.Equal([(SightingStatus)42], query.Statuses);
        Assert.Equal([(SightingCriticality)9], query.Criticalities);
        Assert.Equal(0, query.Page);
        Assert.Equal(15, query.PageSize);
    }

    [Fact]
    public void Map_turns_a_default_request_into_the_first_page_of_10_with_no_filters()
    {
        SightingListQuery query = new SightingListRequestMapper().Map(new SightingListRequestModel { Latitude = 52.1, Longitude = 21.1 });

        Assert.Empty(query.Statuses);
        Assert.Empty(query.Criticalities);
        Assert.Equal(1, query.Page);
        Assert.Equal(SightingRules.ListDefaultPageSize, query.PageSize);
    }
}

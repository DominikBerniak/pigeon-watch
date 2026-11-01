using System.ComponentModel.DataAnnotations;
using PigeonWatch.BusinessObjects;

namespace PigeonWatch.WebApi.Models;

public sealed class SightingListRequestModel
{
    [Range(-90.0, 90.0)]
    public required double Latitude { get; init; }

    [Range(-180.0, 180.0)]
    public required double Longitude { get; init; }

    public List<int> Statuses { get; init; } = [];

    public List<int> Criticalities { get; init; } = [];

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = SightingRules.ListDefaultPageSize;
}

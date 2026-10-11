using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace PigeonWatch.WebApi.Models;

public sealed class NearbySightingsRequestModel
{
    [BindRequired]
    [Range(-90.0, 90.0)]
    public required double Latitude { get; init; }

    [BindRequired]
    [Range(-180.0, 180.0)]
    public required double Longitude { get; init; }
}

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using PigeonWatch.BusinessLogic.Services;
using PigeonWatch.BusinessObjects;
using PigeonWatch.WebApi.Mappers;
using PigeonWatch.WebApi.Models;
using PigeonWatch.WebApi.ViewModelCreators;

namespace PigeonWatch.WebApi.Controllers;

[ApiController]
[Route("sightings")]
[Authorize]
public class SightingsController(
    ISightingService sightingService,
    ICreateSightingRequestMapper createSightingRequestMapper,
    INearbySightingsRequestMapper nearbySightingsRequestMapper,
    ISightingListRequestMapper sightingListRequestMapper,
    ISightingViewModelCreator sightingViewModelCreator,
    ILogger<SightingsController> logger) : FailureLoggingControllerBase(logger)
{
    private const string noStoreCacheControl = "private, no-store";
    private const string jpegContentType = "image/jpeg";

    private static readonly HashSet<string> loggedErrorCodes =
    [
        SightingErrorCodes.InvalidLocation,
        SightingErrorCodes.InvalidInitialStatus,
        SightingErrorCodes.InvalidCriticality,
        SightingErrorCodes.InvalidFilterStatus,
        SightingErrorCodes.InvalidFilterCriticality,
        SightingErrorCodes.InvalidPage,
        SightingErrorCodes.InvalidPageSize,
        SightingErrorCodes.PhotoTooLarge,
        SightingErrorCodes.PhotoUnsupportedType,
        SightingErrorCodes.PhotoUnreadable
    ];

    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(SightingRules.PhotoRequestMaxBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = SightingRules.PhotoRequestMaxBytes)]
    public async Task<ActionResult<SightingModel>> Create([FromForm] CreateSightingRequestModel request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out Guid userId))
            return UnauthorizedForInvalidIdentity();

        NewSighting sighting = createSightingRequestMapper.Map(userId, request);
        await using Stream? photoContent = sighting.Photo?.Content;
        SightingCreateResult result = await sightingService.CreateAsync(sighting, cancellationToken);

        if (result.Errors.Any(error => error.Code == SightingErrorCodes.ReporterNotFound))
            return UnauthorizedForMissingUser();

        if (!result.Succeeded || result.Sighting is null)
        {
            foreach (SightingError error in result.Errors.Where(error => loggedErrorCodes.Contains(error.Code)))
                LogInvalidInput(error.Code);

            return GetValidationProblem<SightingModel>(result.Errors.Select(error => (error.Code, error.Description)), result.RetryAfterSeconds);
        }

        SightingModel model = sightingViewModelCreator.Create(result.Sighting);

        return CreatedAtAction(nameof(Get), new { id = model.Id }, model);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SightingModel>> Get(Guid id, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = noStoreCacheControl;

        Sighting? sighting = await sightingService.GetAsync(id, cancellationToken);

        if (sighting is null)
            return NotFoundForSighting(id);

        return Ok(sightingViewModelCreator.Create(sighting));
    }

    [HttpGet("{id:guid}/photo")]
    [Produces(jpegContentType)]
    public async Task<ActionResult> GetPhoto(Guid id, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = noStoreCacheControl;

        Stream? photo = await sightingService.OpenPhotoAsync(id, cancellationToken);

        if (photo is null)
            return NotFoundForSighting(id);

        return File(photo, jpegContentType);
    }

    [HttpGet("{id:guid}/photo/thumbnail")]
    [Produces(jpegContentType)]
    public async Task<ActionResult> GetPhotoThumbnail(Guid id, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = noStoreCacheControl;

        Stream? thumbnail = await sightingService.OpenPhotoThumbnailAsync(id, cancellationToken);

        if (thumbnail is null)
            return NotFoundForSighting(id);

        return File(thumbnail, jpegContentType);
    }

    [HttpGet("nearby")]
    public async Task<ActionResult<SightingListModel>> Nearby([FromQuery] NearbySightingsRequestModel request, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = noStoreCacheControl;

        NearbySightingFilters filters = nearbySightingsRequestMapper.MapFilters(request);
        SightingNearbyResult result = await sightingService.GetNearbyAsync(request.Latitude, request.Longitude, filters.Statuses, filters.Criticalities, cancellationToken);

        if (!result.Succeeded)
        {
            foreach (SightingError error in result.Errors.Where(error => loggedErrorCodes.Contains(error.Code)))
                LogInvalidInput(error.Code);

            return GetValidationProblem<SightingListModel>(result.Errors.Select(error => (error.Code, error.Description)));
        }

        return Ok(sightingViewModelCreator.CreateList(result.Sightings));
    }

    [HttpPost("list")]
    public async Task<ActionResult<SightingPageModel>> List([FromBody] SightingListRequestModel request, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = noStoreCacheControl;

        SightingListQuery query = sightingListRequestMapper.Map(request);
        SightingListResult result = await sightingService.GetListPageAsync(query, cancellationToken);

        if (!result.Succeeded)
        {
            foreach (SightingError error in result.Errors.Where(error => loggedErrorCodes.Contains(error.Code)))
                LogInvalidInput(error.Code);

            return GetValidationProblem<SightingPageModel>(result.Errors.Select(error => (error.Code, error.Description)));
        }

        return Ok(sightingViewModelCreator.CreatePage(result.Page));
    }

    [HttpGet("mine")]
    public async Task<ActionResult<SightingListModel>> Mine(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = noStoreCacheControl;

        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out Guid userId))
            return UnauthorizedForInvalidIdentity();

        IReadOnlyList<Sighting> sightings = await sightingService.GetMineAsync(userId, cancellationToken);

        return Ok(sightingViewModelCreator.CreateList(sightings));
    }
}

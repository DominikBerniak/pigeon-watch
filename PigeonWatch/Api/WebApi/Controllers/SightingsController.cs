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
    ISightingViewModelCreator sightingViewModelCreator,
    ILogger<SightingsController> logger) : FailureLoggingControllerBase(logger)
{
    private const string noStoreCacheControl = "private, no-store";

    private static readonly HashSet<string> loggedErrorCodes =
    [
        SightingErrorCodes.InvalidLocation,
        SightingErrorCodes.InvalidInitialStatus,
        SightingErrorCodes.InvalidCriticality
    ];

    [HttpPost]
    public async Task<ActionResult<SightingModel>> Create(CreateSightingRequestModel request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out Guid userId))
            return UnauthorizedForInvalidIdentity();

        NewSighting sighting = createSightingRequestMapper.Map(userId, request);
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

    [HttpGet("nearby")]
    public async Task<ActionResult<SightingListModel>> Nearby([FromQuery] NearbySightingsRequestModel request, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = noStoreCacheControl;

        IReadOnlyList<Sighting> sightings = await sightingService.GetNearbyAsync(request.Latitude, request.Longitude, cancellationToken);

        return Ok(sightingViewModelCreator.CreateList(sightings));
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

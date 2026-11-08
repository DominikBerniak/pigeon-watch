using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using PigeonWatch.BusinessLogic.Services;
using PigeonWatch.BusinessObjects;
using PigeonWatch.WebApi.Models;
using PigeonWatch.WebApi.ViewModelCreators;

namespace PigeonWatch.WebApi.Controllers;

[ApiController]
[Route("sightings/{sightingId:guid}/threads")]
[Authorize]
public class SightingThreadsController(
    ISightingThreadService sightingThreadService,
    ISightingThreadViewModelCreator sightingThreadViewModelCreator,
    ILogger<SightingThreadsController> logger) : FailureLoggingControllerBase(logger)
{
    private const string noStoreCacheControl = "private, no-store";

    private static readonly HashSet<string> loggedErrorCodes =
    [
        SightingMessageErrorCodes.OwnSightingContact,
        SightingMessageErrorCodes.InvalidPage
    ];

    [HttpGet]
    public async Task<ActionResult<SightingThreadPageModel>> List(Guid sightingId, [FromQuery] int? page, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = noStoreCacheControl;

        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out Guid userId))
            return UnauthorizedForInvalidIdentity();

        SightingThreadPageResult result = await sightingThreadService.ListForSightingAsync(sightingId, userId, page ?? 1, cancellationToken);

        if (!result.Succeeded)
            return GetFailure<SightingThreadPageModel>(sightingId, null, result.Errors);

        return Ok(sightingThreadViewModelCreator.CreateThreadPage(result.Page, userId));
    }

    [HttpPost]
    public async Task<ActionResult<SightingThreadStartModel>> Start(Guid sightingId, [FromBody] SightingMessageRequestModel request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out Guid userId))
            return UnauthorizedForInvalidIdentity();

        SightingThreadResult result = await sightingThreadService.StartAsync(sightingId, userId, request.Text, cancellationToken);

        if (!result.Succeeded || result.Thread is null || result.Message is null)
            return GetFailure<SightingThreadStartModel>(sightingId, null, result.Errors);

        SightingThreadStartModel model = sightingThreadViewModelCreator.CreateStart(result.Thread, result.Message, userId);

        return CreatedAtAction(nameof(ListMessages), new { sightingId, threadId = model.Thread.Id }, model);
    }

    [HttpGet("{threadId:guid}/messages")]
    public async Task<ActionResult<SightingMessagePageModel>> ListMessages(Guid sightingId, Guid threadId, [FromQuery] int? page, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = noStoreCacheControl;

        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out Guid userId))
            return UnauthorizedForInvalidIdentity();

        SightingMessagePageResult result = await sightingThreadService.ListMessagesAsync(sightingId, threadId, userId, page ?? 1, cancellationToken);

        if (!result.Succeeded)
            return GetFailure<SightingMessagePageModel>(sightingId, threadId, result.Errors);

        return Ok(sightingThreadViewModelCreator.CreateMessagePage(result.Page, userId));
    }

    [HttpPost("{threadId:guid}/messages")]
    public async Task<ActionResult<SightingMessageModel>> Reply(Guid sightingId, Guid threadId, [FromBody] SightingMessageRequestModel request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out Guid userId))
            return UnauthorizedForInvalidIdentity();

        SightingThreadResult result = await sightingThreadService.ReplyAsync(sightingId, threadId, userId, request.Text, cancellationToken);

        if (!result.Succeeded || result.Message is null)
            return GetFailure<SightingMessageModel>(sightingId, threadId, result.Errors);

        SightingMessageModel model = sightingThreadViewModelCreator.CreateMessage(result.Message, userId);

        return CreatedAtAction(nameof(ListMessages), new { sightingId, threadId }, model);
    }

    [HttpPut("{threadId:guid}/read")]
    public async Task<ActionResult<SightingThreadReadModel>> MarkRead(Guid sightingId, Guid threadId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out Guid userId))
            return UnauthorizedForInvalidIdentity();

        SightingThreadReadResult result = await sightingThreadService.MarkReadAsync(sightingId, threadId, userId, cancellationToken);

        if (!result.Succeeded)
            return GetFailure<SightingThreadReadModel>(sightingId, threadId, result.Errors);

        return NoContent();
    }

    private ActionResult<TResponseModel> GetFailure<TResponseModel>(Guid sightingId, Guid? threadId, IReadOnlyList<SightingError> errors)
    {
        HashSet<string> codes = errors.Select(error => error.Code).ToHashSet();

        if (codes.Contains(SightingMessageErrorCodes.SightingNotFound))
            return NotFoundForSighting(sightingId);

        if (codes.Contains(SightingMessageErrorCodes.ThreadNotFound))
            return NotFoundForThread(sightingId, threadId ?? Guid.Empty);

        if (codes.Contains(SightingMessageErrorCodes.SenderNotFound))
            return UnauthorizedForMissingUser();

        foreach (SightingError error in errors.Where(error => loggedErrorCodes.Contains(error.Code)))
            LogInvalidInput(error.Code);

        return GetValidationProblem<TResponseModel>(errors.Select(error => (error.Code, error.Description)));
    }
}

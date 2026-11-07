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
[Route("sightings/{sightingId:guid}/comments")]
[Authorize]
public class SightingCommentsController(
    ISightingCommentService sightingCommentService,
    ISightingCommentViewModelCreator sightingCommentViewModelCreator,
    ILogger<SightingCommentsController> logger) : FailureLoggingControllerBase(logger)
{
    private const string noStoreCacheControl = "private, no-store";

    private static readonly HashSet<string> loggedErrorCodes =
    [
        SightingCommentErrorCodes.InvalidPage,
        SightingCommentErrorCodes.CommentTextTooLong
    ];

    [HttpGet]
    public async Task<ActionResult<SightingCommentPageModel>> List(Guid sightingId, [FromQuery] int? page, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = noStoreCacheControl;

        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out Guid userId))
            return UnauthorizedForInvalidIdentity();

        SightingCommentPageResult result = await sightingCommentService.ListAsync(sightingId, page ?? 1, cancellationToken);

        if (!result.Succeeded)
            return GetFailure<SightingCommentPageModel>(sightingId, null, result.Errors, null);

        return Ok(sightingCommentViewModelCreator.CreatePage(result.Page, userId));
    }

    [HttpPost]
    public async Task<ActionResult<SightingCommentModel>> Create(Guid sightingId, [FromBody] SightingCommentRequestModel request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out Guid userId))
            return UnauthorizedForInvalidIdentity();

        SightingCommentResult result = await sightingCommentService.CreateAsync(sightingId, userId, request.Text, cancellationToken);

        if (!result.Succeeded || result.Comment is null)
            return GetFailure<SightingCommentModel>(sightingId, null, result.Errors, result.RetryAfterSeconds);

        SightingCommentModel model = sightingCommentViewModelCreator.Create(result.Comment, userId);

        return CreatedAtAction(nameof(List), new { sightingId }, model);
    }

    [HttpPut("{commentId:guid}")]
    public async Task<ActionResult<SightingCommentModel>> Update(Guid sightingId, Guid commentId, [FromBody] SightingCommentRequestModel request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out Guid userId))
            return UnauthorizedForInvalidIdentity();

        SightingCommentResult result = await sightingCommentService.UpdateAsync(sightingId, commentId, userId, request.Text, cancellationToken);

        if (!result.Succeeded || result.Comment is null)
            return GetFailure<SightingCommentModel>(sightingId, commentId, result.Errors, result.RetryAfterSeconds);

        return Ok(sightingCommentViewModelCreator.Create(result.Comment, userId));
    }

    [HttpDelete("{commentId:guid}")]
    public async Task<ActionResult<SightingCommentModel>> Delete(Guid sightingId, Guid commentId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out Guid userId))
            return UnauthorizedForInvalidIdentity();

        SightingCommentResult result = await sightingCommentService.DeleteAsync(sightingId, commentId, userId, cancellationToken);

        if (!result.Succeeded || result.Comment is null)
            return GetFailure<SightingCommentModel>(sightingId, commentId, result.Errors, null);

        return Ok(sightingCommentViewModelCreator.Create(result.Comment, userId));
    }

    private ActionResult<TResponseModel> GetFailure<TResponseModel>(Guid sightingId, Guid? commentId, IReadOnlyList<SightingError> errors, int? retryAfterSeconds)
    {
        HashSet<string> codes = errors.Select(error => error.Code).ToHashSet();

        if (codes.Contains(SightingCommentErrorCodes.SightingNotFound))
            return NotFoundForSighting(sightingId);

        if (codes.Contains(SightingCommentErrorCodes.CommentNotFound))
            return NotFoundForComment(sightingId, commentId ?? Guid.Empty);

        if (codes.Contains(SightingCommentErrorCodes.NotCommentAuthor))
            return ForbiddenForNonAuthor();

        if (codes.Contains(SightingCommentErrorCodes.AuthorNotFound))
            return UnauthorizedForMissingUser();

        foreach (SightingError error in errors.Where(error => loggedErrorCodes.Contains(error.Code)))
            LogInvalidInput(error.Code);

        return GetValidationProblem<TResponseModel>(errors.Select(error => (error.Code, error.Description)), retryAfterSeconds);
    }
}

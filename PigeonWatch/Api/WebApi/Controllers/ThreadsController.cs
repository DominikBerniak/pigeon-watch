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
[Route("threads")]
[Authorize]
public class ThreadsController(
    ISightingThreadService sightingThreadService,
    ISightingThreadViewModelCreator sightingThreadViewModelCreator,
    ILogger<ThreadsController> logger) : FailureLoggingControllerBase(logger)
{
    private const string noStoreCacheControl = "private, no-store";

    private static readonly HashSet<string> loggedErrorCodes =
    [
        SightingMessageErrorCodes.InvalidPage
    ];

    [HttpGet]
    public async Task<ActionResult<SightingThreadPageModel>> List([FromQuery] int? page, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = noStoreCacheControl;

        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out Guid userId))
            return UnauthorizedForInvalidIdentity();

        SightingThreadPageResult result = await sightingThreadService.ListForUserAsync(userId, page ?? 1, cancellationToken);

        if (!result.Succeeded)
        {
            foreach (SightingError error in result.Errors.Where(error => loggedErrorCodes.Contains(error.Code)))
                LogInvalidInput(error.Code);

            return GetValidationProblem<SightingThreadPageModel>(result.Errors.Select(error => (error.Code, error.Description)));
        }

        return Ok(sightingThreadViewModelCreator.CreateThreadPage(result.Page, userId));
    }
}

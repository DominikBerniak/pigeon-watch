using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PigeonWatch.BusinessLogic.Services;
using PigeonWatch.BusinessObjects;
using PigeonWatch.WebApi.Mappers;
using PigeonWatch.WebApi.Models;
using PigeonWatch.WebApi.RateLimiting;
using PigeonWatch.WebApi.ViewModelCreators;
using System.Security.Claims;

namespace PigeonWatch.WebApi.Controllers;

[ApiController]
[Route("account")]
public class AccountController(
    IAccountService accountService,
    IRegisterRequestMapper registerRequestMapper,
    IRegisteredAccountViewModelCreator registeredAccountViewModelCreator,
    IUpdatedProfileViewModelCreator updatedProfileViewModelCreator) : ControllerBase
{
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicyNames.Auth)]
    public async Task<ActionResult<RegisteredAccountModel>> Register(RegisterRequestModel request, CancellationToken cancellationToken)
    {
        NewAccount account = registerRequestMapper.Map(request);
        AccountCreationResult result = await accountService.RegisterAsync(account, cancellationToken);

        if (!result.Succeeded || result.Account is null)
            return GetValidationProblem<RegisteredAccountModel>(result.Errors);

        return Ok(registeredAccountViewModelCreator.Create(result.Account));
    }

    [HttpPut("profile")]
    [Authorize]
    public async Task<ActionResult<UpdatedProfileModel>> UpdateProfile(UpdateProfileRequestModel request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out Guid userId))
            return Unauthorized();

        ProfileUpdateResult result = await accountService.UpdateDisplayNameAsync(userId, request.DisplayName, cancellationToken);

        if (result.Errors.Any(error => error.Code == AccountErrorCodes.UserNotFound))
            return Unauthorized();

        if (!result.Succeeded || result.Profile is null)
            return GetValidationProblem<UpdatedProfileModel>(result.Errors);

        return Ok(updatedProfileViewModelCreator.Create(result.Profile));
    }

    private ActionResult<TResponseModel> GetValidationProblem<TResponseModel>(IReadOnlyList<AccountError> errors)
    {
        foreach (AccountError error in errors)
            ModelState.AddModelError(error.Code, error.Description);

        return ValidationProblem(ModelState);
    }
}

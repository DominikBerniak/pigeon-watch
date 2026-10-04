using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PigeonWatch.BusinessLogic.Services;
using PigeonWatch.BusinessObjects;
using PigeonWatch.WebApi.Mappers;
using PigeonWatch.WebApi.Models;
using PigeonWatch.WebApi.RateLimiting;
using PigeonWatch.WebApi.ViewModelCreators;

namespace PigeonWatch.WebApi.Controllers;

[ApiController]
[Route("account")]
public class AccountController(
    IAccountService accountService,
    IRegisterRequestMapper registerRequestMapper,
    IRegisteredAccountViewModelCreator registeredAccountViewModelCreator) : ControllerBase
{
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicyNames.Auth)]
    public async Task<ActionResult<RegisteredAccountModel>> Register(RegisterRequestModel request, CancellationToken cancellationToken)
    {
        NewAccount account = registerRequestMapper.Map(request);
        AccountCreationResult result = await accountService.RegisterAsync(account, cancellationToken);

        if (!result.Succeeded || result.Account is null)
        {
            foreach (AccountError error in result.Errors)
                ModelState.AddModelError(error.Code, error.Description);

            return ValidationProblem(ModelState);
        }

        return Ok(registeredAccountViewModelCreator.Create(result.Account));
    }
}

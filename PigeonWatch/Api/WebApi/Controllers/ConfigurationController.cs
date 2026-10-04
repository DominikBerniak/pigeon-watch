using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PigeonWatch.BusinessLogic.Services;
using PigeonWatch.BusinessObjects;
using PigeonWatch.WebApi.Models;
using PigeonWatch.WebApi.ViewModelCreators;

namespace PigeonWatch.WebApi.Controllers;

[ApiController]
[Route("configuration")]
public class ConfigurationController(
    IClientConfigurationService clientConfigurationService,
    IGeneralConfigurationService generalConfigurationService,
    IClientConfigurationViewModelCreator clientConfigurationViewModelCreator,
    IGeneralConfigurationViewModelCreator generalConfigurationViewModelCreator) : ControllerBase
{
    [HttpGet("client")]
    [AllowAnonymous]
    public async Task<ActionResult<ClientConfigurationModel>> GetClient(CancellationToken cancellationToken)
    {
        ClientConfiguration configuration = await clientConfigurationService.GetAsync(cancellationToken);
        Response.Headers.CacheControl = "public, max-age=300";

        return Ok(clientConfigurationViewModelCreator.Create(configuration));
    }

    [HttpGet("general")]
    [Authorize]
    public async Task<ActionResult<GeneralConfigurationModel>> GetGeneral(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "private, no-store";

        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out Guid userId))
            return Unauthorized();

        CurrentUser? currentUser = await generalConfigurationService.GetAsync(userId, cancellationToken);

        if (currentUser is null)
            return Unauthorized();

        List<string> roles = User.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToList();

        return Ok(generalConfigurationViewModelCreator.Create(currentUser, roles));
    }
}

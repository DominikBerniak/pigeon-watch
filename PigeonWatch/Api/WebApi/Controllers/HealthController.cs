using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PigeonWatch.BusinessLogic.Services;
using PigeonWatch.BusinessObjects;
using PigeonWatch.WebApi.Models;
using PigeonWatch.WebApi.ViewModelCreators;

namespace PigeonWatch.WebApi.Controllers;

[ApiController]
[Route("health/db")]
public class HealthController(
    IDatabaseHealthService databaseHealthService,
    IDatabaseHealthViewModelCreator databaseHealthViewModelCreator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<DatabaseHealthModel>> Get(CancellationToken cancellationToken)
    {
        DatabaseHealthResult result = await databaseHealthService.CheckAsync(cancellationToken);
        DatabaseHealthModel model = databaseHealthViewModelCreator.Create(result);

        if (!result.IsHealthy)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, model);
        }

        return Ok(model);
    }
}

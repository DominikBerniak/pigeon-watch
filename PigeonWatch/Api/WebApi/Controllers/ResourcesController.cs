using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PigeonWatch.BusinessLogic.Services;
using PigeonWatch.BusinessObjects;
using PigeonWatch.WebApi.Models;
using PigeonWatch.WebApi.ViewModelCreators;

namespace PigeonWatch.WebApi.Controllers;

[ApiController]
[Route("resources")]
public class ResourcesController(
    IUiResourceService uiResourceService,
    IUiResourcesViewModelCreator uiResourcesViewModelCreator) : ControllerBase
{
    [HttpGet("{culture}")]
    [AllowAnonymous]
    public async Task<ActionResult<UiResourcesModel>> Get(string culture, CancellationToken cancellationToken)
    {
        UiLabelSet labelSet = await uiResourceService.GetAsync(culture, cancellationToken);
        Response.Headers.CacheControl = "public, max-age=300";
        Response.Headers.ContentLanguage = labelSet.Culture;

        return Ok(uiResourcesViewModelCreator.Create(labelSet));
    }
}

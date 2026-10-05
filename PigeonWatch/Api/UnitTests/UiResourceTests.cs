using System.Xml.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PigeonWatch.BusinessLogic.Services;
using PigeonWatch.BusinessObjects;
using PigeonWatch.WebApi.Controllers;
using PigeonWatch.WebApi.Models;
using PigeonWatch.WebApi.ViewModelCreators;

namespace PigeonWatch.UnitTests;

public class UiResourceTests
{
    [Fact]
    public async Task English_returns_every_key_in_the_resx()
    {
        Dictionary<string, string> expected = ReadNeutralResx();

        UiLabelSet labelSet = await new UiResourceService().GetAsync("en", TestContext.Current.CancellationToken);

        Assert.Equal(UiResources.DefaultCulture, labelSet.Culture);
        Assert.Equal(expected.OrderBy(entry => entry.Key, StringComparer.Ordinal), labelSet.Labels.OrderBy(entry => entry.Key, StringComparer.Ordinal));
        Assert.Equal("Use at least {0} characters.", labelSet.Labels["auth.validation.passwordMinLength"]);
    }

    [Theory]
    [InlineData("xx")]
    [InlineData("pl")]
    [InlineData("EN")]
    [InlineData("en-US")]
    [InlineData("not a culture!")]
    [InlineData("zz-q1")]
    [InlineData("qaa-AB")]
    public async Task Unsupported_cultures_resolve_to_english(string culture)
    {
        UiResourceService service = new();
        UiLabelSet english = await service.GetAsync("en", TestContext.Current.CancellationToken);

        UiLabelSet labelSet = await service.GetAsync(culture, TestContext.Current.CancellationToken);

        Assert.Equal("en", labelSet.Culture);
        Assert.Equal(english.Labels, labelSet.Labels);
    }

    [Fact]
    public async Task Resources_controller_returns_the_culture_and_the_map()
    {
        ResourcesController controller = new(new UiResourceService(), new UiResourcesViewModelCreator())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        ActionResult<UiResourcesModel> result = await controller.Get("xx", TestContext.Current.CancellationToken);

        UiResourcesModel model = Assert.IsType<UiResourcesModel>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("en", model.Culture);
        Assert.Equal("PigeonWatch", model.Labels["common.appName"]);
        Assert.Equal(ReadNeutralResx().Count, model.Labels.Count);
        Assert.Equal("en", controller.Response.Headers.ContentLanguage.ToString());
        Assert.Equal("public, max-age=300", controller.Response.Headers.CacheControl.ToString());
    }

    private static Dictionary<string, string> ReadNeutralResx()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BusinessObjects", "Resources", "UiLabels.resx")))
            directory = directory.Parent;

        Assert.NotNull(directory);

        return XDocument.Load(Path.Combine(directory.FullName, "BusinessObjects", "Resources", "UiLabels.resx"))
            .Root!
            .Elements("data")
            .ToDictionary(data => (string)data.Attribute("name")!, data => (string)data.Element("value")!);
    }
}

using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NSubstitute;
using PigeonWatch.BusinessLogic.Services;
using PigeonWatch.BusinessObjects;
using PigeonWatch.WebApi.Controllers;
using PigeonWatch.WebApi.Mappers;
using PigeonWatch.WebApi.Models;
using PigeonWatch.WebApi.ViewModelCreators;

namespace PigeonWatch.UnitTests;

public class SightingsControllerTests
{
    private readonly ISightingService sightingService = Substitute.For<ISightingService>();
    private readonly CancellationToken cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Create_returns_201_with_the_created_sighting()
    {
        Guid userId = Guid.NewGuid();
        Sighting created = Sighting();
        sightingService
            .CreateAsync(Arg.Any<NewSighting>(), Arg.Any<CancellationToken>())
            .Returns(SightingCreateResult.Success(created));
        SightingsController controller = CreateController(Authenticated(userId));

        ActionResult<SightingModel> result = await controller.Create(Request(), cancellationToken);

        CreatedAtActionResult createdResult = Assert.IsType<CreatedAtActionResult>(result.Result);
        SightingModel model = Assert.IsType<SightingModel>(createdResult.Value);
        Assert.Equal(StatusCodes.Status201Created, createdResult.StatusCode);
        Assert.Equal(nameof(SightingsController.Get), createdResult.ActionName);
        Assert.Equal(created.Id, createdResult.RouteValues!["id"]);
        Assert.Equal(created.Id, model.Id);
        Assert.Equal("Grey", model.Name);
        Assert.Equal("Limping near the fountain", model.Description);
        Assert.Equal("Cannot fly", model.Behaviour);
        Assert.Equal(52.2297, model.Latitude);
        Assert.Equal(21.0122, model.Longitude);
        Assert.Equal(2, model.Status);
        Assert.Equal(3, model.Criticality);
        Assert.Equal("Pidgey", model.ReporterDisplayName);
        Assert.Equal(created.CreatedAt, model.CreatedAt);
    }

    [Fact]
    public async Task Create_maps_the_request_with_the_current_user_as_reporter()
    {
        Guid userId = Guid.NewGuid();
        sightingService
            .CreateAsync(Arg.Any<NewSighting>(), Arg.Any<CancellationToken>())
            .Returns(SightingCreateResult.Success(Sighting()));
        SightingsController controller = CreateController(Authenticated(userId));

        await controller.Create(Request(), cancellationToken);

        await sightingService.Received(1).CreateAsync(
            new NewSighting(userId, "Grey", "Limping near the fountain", "Cannot fly", 52.2297, 21.0122, SightingStatus.Caught, SightingCriticality.Urgent),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_returns_400_with_retry_after_seconds_while_the_cooldown_is_active()
    {
        sightingService
            .CreateAsync(Arg.Any<NewSighting>(), Arg.Any<CancellationToken>())
            .Returns(SightingCreateResult.CooldownFailure(new SightingError(SightingErrorCodes.CooldownActive, "wait"), 42));
        SightingsController controller = CreateController(Authenticated(Guid.NewGuid()));

        ActionResult<SightingModel> result = await controller.Create(Request(), cancellationToken);

        ValidationProblemDetails problem = Assert.IsType<ValidationProblemDetails>(Assert.IsType<BadRequestObjectResult>(result.Result).Value);
        Assert.Equal([SightingErrorCodes.CooldownActive], problem.Errors.Keys);
        Assert.Equal(42, problem.Extensions["retryAfterSeconds"]);
        Assert.Equal("42", controller.Response.Headers.RetryAfter.ToString());
    }

    [Fact]
    public async Task Create_returns_400_keyed_by_code_without_retry_after_for_a_validation_failure()
    {
        sightingService
            .CreateAsync(Arg.Any<NewSighting>(), Arg.Any<CancellationToken>())
            .Returns(SightingCreateResult.Failure(
            [
                new SightingError(SightingErrorCodes.InvalidInitialStatus, "status"),
                new SightingError(SightingErrorCodes.InvalidCriticality, "criticality")
            ]));
        SightingsController controller = CreateController(Authenticated(Guid.NewGuid()));

        ActionResult<SightingModel> result = await controller.Create(Request(), cancellationToken);

        ValidationProblemDetails problem = Assert.IsType<ValidationProblemDetails>(Assert.IsType<BadRequestObjectResult>(result.Result).Value);
        Assert.Equal(
            [SightingErrorCodes.InvalidCriticality, SightingErrorCodes.InvalidInitialStatus],
            problem.Errors.Keys.Order(StringComparer.Ordinal));
        Assert.False(problem.Extensions.ContainsKey("retryAfterSeconds"));
        Assert.False(controller.Response.Headers.ContainsKey("Retry-After"));
    }

    [Fact]
    public async Task Create_returns_401_when_the_name_identifier_claim_is_missing()
    {
        SightingsController controller = CreateController(new ClaimsPrincipal(new ClaimsIdentity()));

        ActionResult<SightingModel> result = await controller.Create(Request(), cancellationToken);

        Assert.IsType<UnauthorizedResult>(result.Result);
        await sightingService.DidNotReceive().CreateAsync(Arg.Any<NewSighting>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_returns_401_when_the_reporter_no_longer_exists()
    {
        sightingService
            .CreateAsync(Arg.Any<NewSighting>(), Arg.Any<CancellationToken>())
            .Returns(SightingCreateResult.Failure([new SightingError(SightingErrorCodes.ReporterNotFound, "missing")]));
        SightingsController controller = CreateController(Authenticated(Guid.NewGuid()));

        ActionResult<SightingModel> result = await controller.Create(Request(), cancellationToken);

        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Fact]
    public async Task Get_returns_the_sighting_without_caching()
    {
        Sighting stored = Sighting();
        sightingService.GetAsync(stored.Id, Arg.Any<CancellationToken>()).Returns(stored);
        SightingsController controller = CreateController(Authenticated(Guid.NewGuid()));

        ActionResult<SightingModel> result = await controller.Get(stored.Id, cancellationToken);

        SightingModel model = Assert.IsType<SightingModel>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(stored.Id, model.Id);
        Assert.Equal("private, no-store", controller.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task Get_returns_404_for_an_unknown_id()
    {
        Guid unknownId = Guid.NewGuid();
        sightingService.GetAsync(unknownId, Arg.Any<CancellationToken>()).Returns((Sighting?)null);
        SightingsController controller = CreateController(Authenticated(Guid.NewGuid()));

        ActionResult<SightingModel> result = await controller.Get(unknownId, cancellationToken);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Nearby_returns_the_sightings_around_the_requested_centre_without_caching()
    {
        Sighting stored = Sighting();
        sightingService.GetNearbyAsync(52.1, 21.1, Arg.Any<CancellationToken>()).Returns([stored]);
        SightingsController controller = CreateController(Authenticated(Guid.NewGuid()));

        ActionResult<SightingListModel> result = await controller.Nearby(
            new NearbySightingsRequestModel { Latitude = 52.1, Longitude = 21.1 },
            cancellationToken);

        SightingListModel model = Assert.IsType<SightingListModel>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal([stored.Id], model.Items.Select(item => item.Id));
        Assert.Equal("private, no-store", controller.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task Mine_returns_the_current_users_sightings_without_caching()
    {
        Guid userId = Guid.NewGuid();
        Sighting stored = Sighting();
        sightingService.GetMineAsync(userId, Arg.Any<CancellationToken>()).Returns([stored]);
        SightingsController controller = CreateController(Authenticated(userId));

        ActionResult<SightingListModel> result = await controller.Mine(cancellationToken);

        SightingListModel model = Assert.IsType<SightingListModel>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal([stored.Id], model.Items.Select(item => item.Id));
        Assert.Equal("private, no-store", controller.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task Mine_returns_401_when_the_name_identifier_claim_is_missing()
    {
        SightingsController controller = CreateController(new ClaimsPrincipal(new ClaimsIdentity()));

        ActionResult<SightingListModel> result = await controller.Mine(cancellationToken);

        Assert.IsType<UnauthorizedResult>(result.Result);
        await sightingService.DidNotReceive().GetMineAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Sighting_model_exposes_no_reporter_id_or_email_and_no_business_object_types()
    {
        PropertyInfo[] properties = typeof(SightingModel).GetProperties();

        Assert.DoesNotContain(properties, property => property.Name.Contains("Email", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, property => property.Name.Contains("ReporterId", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, property => property.PropertyType.Assembly == typeof(Sighting).Assembly);
        Assert.Equal(typeof(int), typeof(SightingModel).GetProperty(nameof(SightingModel.Status))!.PropertyType);
        Assert.Equal(typeof(int), typeof(SightingModel).GetProperty(nameof(SightingModel.Criticality))!.PropertyType);
    }

    private SightingsController CreateController(ClaimsPrincipal user)
    {
        ProblemDetailsFactory problemDetailsFactory = Substitute.For<ProblemDetailsFactory>();
        problemDetailsFactory
            .CreateValidationProblemDetails(
                Arg.Any<HttpContext>(),
                Arg.Any<ModelStateDictionary>(),
                Arg.Any<int?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>())
            .Returns(call => new ValidationProblemDetails(call.Arg<ModelStateDictionary>()) { Status = StatusCodes.Status400BadRequest });

        return new(sightingService, new CreateSightingRequestMapper(), new SightingViewModelCreator())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } },
            ProblemDetailsFactory = problemDetailsFactory
        };
    }

    private static CreateSightingRequestModel Request()
    {
        return new()
        {
            Name = "Grey",
            Description = "Limping near the fountain",
            Behaviour = "Cannot fly",
            Latitude = 52.2297,
            Longitude = 21.0122,
            Status = 2,
            Criticality = 3
        };
    }

    private static Sighting Sighting()
    {
        return new(
            Guid.NewGuid(),
            "Grey",
            "Limping near the fountain",
            "Cannot fly",
            52.2297,
            21.0122,
            SightingStatus.Caught,
            SightingCriticality.Urgent,
            "Pidgey",
            new DateTime(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc));
    }

    private static ClaimsPrincipal Authenticated(Guid userId)
    {
        return new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Bearer"));
    }
}

using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.Core;
using PigeonWatch.BusinessLogic.Services;
using PigeonWatch.BusinessObjects;
using PigeonWatch.WebApi.Controllers;
using PigeonWatch.WebApi.Mappers;
using PigeonWatch.WebApi.Models;
using PigeonWatch.WebApi.ViewModelCreators;

namespace PigeonWatch.UnitTests;

public class ControllerFailureLoggingTests
{
    private readonly ISightingService sightingService = Substitute.For<ISightingService>();
    private readonly IAccountService accountService = Substitute.For<IAccountService>();
    private readonly IGeneralConfigurationService generalConfigurationService = Substitute.For<IGeneralConfigurationService>();
    private readonly ILogger<SightingsController> sightingsLogger = Substitute.For<ILogger<SightingsController>>();
    private readonly ILogger<AccountController> accountLogger = Substitute.For<ILogger<AccountController>>();
    private readonly ILogger<ConfigurationController> configurationLogger = Substitute.For<ILogger<ConfigurationController>>();
    private readonly CancellationToken cancellationToken = TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(SightingErrorCodes.InvalidLocation)]
    [InlineData(SightingErrorCodes.InvalidInitialStatus)]
    [InlineData(SightingErrorCodes.InvalidCriticality)]
    public async Task Create_logs_a_warning_with_the_error_code_for_invalid_input(string code)
    {
        sightingService
            .CreateAsync(Arg.Any<NewSighting>(), Arg.Any<CancellationToken>())
            .Returns(SightingCreateResult.Failure([new SightingError(code, "invalid")]));
        SightingsController controller = CreateSightingsController(Authenticated(Guid.NewGuid()));

        ActionResult<SightingModel> result = await controller.Create(Request(), cancellationToken);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        LogCall call = Assert.Single(LogCalls(sightingsLogger));
        Assert.Equal(LogLevel.Warning, call.Level);
        Assert.Equal(code, call.State["ErrorCode"]);
        Assert.DoesNotContain(code, call.Message);
    }

    [Fact]
    public async Task Create_logs_nothing_for_a_cooldown_rejection()
    {
        sightingService
            .CreateAsync(Arg.Any<NewSighting>(), Arg.Any<CancellationToken>())
            .Returns(SightingCreateResult.CooldownFailure(new SightingError(SightingErrorCodes.CooldownActive, "wait"), 42));
        SightingsController controller = CreateSightingsController(Authenticated(Guid.NewGuid()));

        ActionResult<SightingModel> result = await controller.Create(Request(), cancellationToken);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(LogCalls(sightingsLogger));
    }

    [Fact]
    public async Task Create_logs_a_warning_when_the_reporter_no_longer_exists()
    {
        sightingService
            .CreateAsync(Arg.Any<NewSighting>(), Arg.Any<CancellationToken>())
            .Returns(SightingCreateResult.Failure([new SightingError(SightingErrorCodes.ReporterNotFound, "missing")]));
        SightingsController controller = CreateSightingsController(Authenticated(Guid.NewGuid()));

        ActionResult<SightingModel> result = await controller.Create(Request(), cancellationToken);

        Assert.IsType<UnauthorizedResult>(result.Result);
        LogCall call = Assert.Single(LogCalls(sightingsLogger));
        Assert.Equal(LogLevel.Warning, call.Level);
        Assert.Contains("Route", call.State.Keys);
    }

    [Fact]
    public async Task Create_logs_a_warning_when_the_name_identifier_claim_is_missing()
    {
        SightingsController controller = CreateSightingsController(new ClaimsPrincipal(new ClaimsIdentity()));

        ActionResult<SightingModel> result = await controller.Create(Request(), cancellationToken);

        Assert.IsType<UnauthorizedResult>(result.Result);
        LogCall call = Assert.Single(LogCalls(sightingsLogger));
        Assert.Equal(LogLevel.Warning, call.Level);
        Assert.Contains("Route", call.State.Keys);
    }

    [Fact]
    public async Task Get_logs_one_information_with_the_sighting_id_when_it_is_missing()
    {
        Guid id = Guid.NewGuid();
        sightingService.GetAsync(id, Arg.Any<CancellationToken>()).Returns((Sighting?)null);
        SightingsController controller = CreateSightingsController(Authenticated(Guid.NewGuid()));

        ActionResult<SightingModel> result = await controller.Get(id, cancellationToken);

        Assert.IsType<NotFoundResult>(result.Result);
        LogCall call = Assert.Single(LogCalls(sightingsLogger));
        Assert.Equal(LogLevel.Information, call.Level);
        Assert.Equal(id, call.State["SightingId"]);
    }

    [Fact]
    public async Task Update_profile_logs_a_warning_when_the_user_no_longer_exists()
    {
        Guid userId = Guid.NewGuid();
        accountService
            .UpdateDisplayNameAsync(userId, "Feathers", Arg.Any<CancellationToken>())
            .Returns(ProfileUpdateResult.Failure([new AccountError(AccountErrorCodes.UserNotFound, "missing")]));
        AccountController controller = CreateAccountController(Authenticated(userId));

        ActionResult<UpdatedProfileModel> result = await controller.UpdateProfile(
            new UpdateProfileRequestModel { DisplayName = "Feathers" },
            cancellationToken);

        Assert.IsType<UnauthorizedResult>(result.Result);
        LogCall call = Assert.Single(LogCalls(accountLogger));
        Assert.Equal(LogLevel.Warning, call.Level);
    }

    [Fact]
    public async Task Change_password_logs_a_warning_when_the_name_identifier_claim_is_malformed()
    {
        ClaimsPrincipal user = new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "not-a-guid")], "Bearer"));
        AccountController controller = CreateAccountController(user);

        ActionResult<PasswordChangedModel> result = await controller.ChangePassword(
            new ChangePasswordRequestModel { CurrentPassword = "Old1!", NewPassword = "New1Password!" },
            cancellationToken);

        Assert.IsType<UnauthorizedResult>(result.Result);
        LogCall call = Assert.Single(LogCalls(accountLogger));
        Assert.Equal(LogLevel.Warning, call.Level);
        Assert.DoesNotContain("not-a-guid", call.Message);
    }

    [Fact]
    public async Task Update_profile_logs_nothing_for_a_taken_display_name()
    {
        Guid userId = Guid.NewGuid();
        accountService
            .UpdateDisplayNameAsync(userId, "Feathers", Arg.Any<CancellationToken>())
            .Returns(ProfileUpdateResult.Failure([new AccountError(AccountErrorCodes.DuplicateDisplayName, "taken")]));
        AccountController controller = CreateAccountController(Authenticated(userId));

        ActionResult<UpdatedProfileModel> result = await controller.UpdateProfile(
            new UpdateProfileRequestModel { DisplayName = "Feathers" },
            cancellationToken);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(LogCalls(accountLogger));
    }

    [Fact]
    public async Task General_endpoint_logs_a_warning_when_the_user_no_longer_exists()
    {
        Guid userId = Guid.NewGuid();
        generalConfigurationService.GetAsync(userId, Arg.Any<CancellationToken>()).Returns((CurrentUser?)null);
        ConfigurationController controller = CreateConfigurationController(Authenticated(userId));

        ActionResult<GeneralConfigurationModel> result = await controller.GetGeneral(cancellationToken);

        Assert.IsType<UnauthorizedResult>(result.Result);
        LogCall call = Assert.Single(LogCalls(configurationLogger));
        Assert.Equal(LogLevel.Warning, call.Level);
    }

    [Fact]
    public async Task General_endpoint_logs_a_warning_when_the_name_identifier_claim_is_missing()
    {
        ConfigurationController controller = CreateConfigurationController(new ClaimsPrincipal(new ClaimsIdentity()));

        ActionResult<GeneralConfigurationModel> result = await controller.GetGeneral(cancellationToken);

        Assert.IsType<UnauthorizedResult>(result.Result);
        LogCall call = Assert.Single(LogCalls(configurationLogger));
        Assert.Equal(LogLevel.Warning, call.Level);
    }

    private SightingsController CreateSightingsController(ClaimsPrincipal user)
    {
        return new(sightingService, new CreateSightingRequestMapper(), new SightingViewModelCreator(), sightingsLogger)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } },
            ProblemDetailsFactory = CreateProblemDetailsFactory()
        };
    }

    private AccountController CreateAccountController(ClaimsPrincipal user)
    {
        return new(
            accountService,
            new RegisterRequestMapper(),
            new RegisteredAccountViewModelCreator(),
            new UpdatedProfileViewModelCreator(),
            new PasswordChangedViewModelCreator(),
            accountLogger)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } },
            ProblemDetailsFactory = CreateProblemDetailsFactory()
        };
    }

    private ConfigurationController CreateConfigurationController(ClaimsPrincipal user)
    {
        return new(
            new ClientConfigurationService(),
            generalConfigurationService,
            new ClientConfigurationViewModelCreator(),
            new GeneralConfigurationViewModelCreator(),
            configurationLogger)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } }
        };
    }

    private static ProblemDetailsFactory CreateProblemDetailsFactory()
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

        return problemDetailsFactory;
    }

    private static List<LogCall> LogCalls(ILogger logger)
    {
        List<LogCall> calls = logger.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(ILogger.Log))
            .Select(ToLogCall)
            .ToList();

        return calls;
    }

    private static LogCall ToLogCall(ICall call)
    {
        object?[] arguments = call.GetArguments();
        Dictionary<string, object?> state = [];

        foreach (KeyValuePair<string, object?> pair in (IEnumerable<KeyValuePair<string, object?>>)arguments[2]!)
            state[pair.Key] = pair.Value;

        string message = state["{OriginalFormat}"]?.ToString() ?? string.Empty;

        return new LogCall((LogLevel)arguments[0]!, state, message);
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

    private static ClaimsPrincipal Authenticated(Guid userId)
    {
        return new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Bearer"));
    }

    private sealed record LogCall(LogLevel Level, Dictionary<string, object?> State, string Message);
}

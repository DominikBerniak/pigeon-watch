using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using PigeonWatch.BusinessLogic.Services;
using PigeonWatch.BusinessObjects;
using PigeonWatch.Data.Repositories;
using PigeonWatch.WebApi.Controllers;
using PigeonWatch.WebApi.Models;
using PigeonWatch.WebApi.ViewModelCreators;

namespace PigeonWatch.UnitTests;

public class ConfigurationTests
{
    private readonly IAccountRepository accountRepository = Substitute.For<IAccountRepository>();

    [Fact]
    public async Task Client_configuration_matches_account_rules()
    {
        ClientConfiguration configuration = await new ClientConfigurationService().GetAsync(TestContext.Current.CancellationToken);

        ClientConfigurationModel model = new ClientConfigurationViewModelCreator().Create(configuration);

        Assert.Equal(AccountRules.PasswordMinLength, model.PasswordRules.MinLength);
        Assert.Equal(AccountRules.PasswordRequiresDigit, model.PasswordRules.RequireDigit);
        Assert.Equal(AccountRules.PasswordRequiresLowercase, model.PasswordRules.RequireLowercase);
        Assert.Equal(AccountRules.PasswordRequiresUppercase, model.PasswordRules.RequireUppercase);
        Assert.Equal(AccountRules.PasswordRequiresNonAlphanumeric, model.PasswordRules.RequireNonAlphanumeric);
        Assert.Equal(AccountRules.DisplayNameMinLength, model.DisplayNameRules.MinLength);
        Assert.Equal(AccountRules.DisplayNameMaxLength, model.DisplayNameRules.MaxLength);
    }

    [Fact]
    public async Task General_configuration_service_returns_null_for_an_unknown_id()
    {
        Guid unknownId = Guid.NewGuid();
        accountRepository.GetCurrentUserAsync(unknownId, Arg.Any<CancellationToken>()).Returns((CurrentUser?)null);

        CurrentUser? currentUser = await new GeneralConfigurationService(accountRepository)
            .GetAsync(unknownId, TestContext.Current.CancellationToken);

        Assert.Null(currentUser);
    }

    [Fact]
    public async Task General_configuration_service_returns_the_current_user()
    {
        CurrentUser expected = new(Guid.NewGuid(), "user@example.com", "Pidgey");
        accountRepository.GetCurrentUserAsync(expected.Id, Arg.Any<CancellationToken>()).Returns(expected);

        CurrentUser? currentUser = await new GeneralConfigurationService(accountRepository)
            .GetAsync(expected.Id, TestContext.Current.CancellationToken);

        Assert.Equal(expected, currentUser);
    }

    [Fact]
    public async Task Client_endpoint_is_publicly_cacheable()
    {
        ConfigurationController controller = CreateController(new ClaimsPrincipal(new ClaimsIdentity()));

        ActionResult<ClientConfigurationModel> result = await controller.GetClient(TestContext.Current.CancellationToken);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("public, max-age=300", controller.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task General_endpoint_returns_the_user_with_roles_from_claims()
    {
        CurrentUser user = new(Guid.NewGuid(), "user@example.com", "Pidgey");
        accountRepository.GetCurrentUserAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        ConfigurationController controller = CreateController(Authenticated(user.Id));

        ActionResult<GeneralConfigurationModel> result = await controller.GetGeneral(TestContext.Current.CancellationToken);

        GeneralConfigurationModel model = Assert.IsType<GeneralConfigurationModel>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(user.Id, model.CurrentUser.Id);
        Assert.Equal("user@example.com", model.CurrentUser.Email);
        Assert.Equal("Pidgey", model.CurrentUser.DisplayName);
        Assert.Empty(model.CurrentUser.Roles);
        Assert.Equal("private, no-store", controller.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task General_endpoint_returns_401_for_an_unknown_user()
    {
        Guid unknownId = Guid.NewGuid();
        accountRepository.GetCurrentUserAsync(unknownId, Arg.Any<CancellationToken>()).Returns((CurrentUser?)null);
        ConfigurationController controller = CreateController(Authenticated(unknownId));

        ActionResult<GeneralConfigurationModel> result = await controller.GetGeneral(TestContext.Current.CancellationToken);

        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    private ConfigurationController CreateController(ClaimsPrincipal user)
    {
        return new(
            new ClientConfigurationService(),
            new GeneralConfigurationService(accountRepository),
            new ClientConfigurationViewModelCreator(),
            new GeneralConfigurationViewModelCreator())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } }
        };
    }

    private static ClaimsPrincipal Authenticated(Guid userId)
    {
        return new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Bearer"));
    }
}

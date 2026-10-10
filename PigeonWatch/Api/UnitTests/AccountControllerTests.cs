using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NSubstitute;
using PigeonWatch.BusinessLogic.Services;
using PigeonWatch.BusinessObjects;
using PigeonWatch.Data.Repositories;
using PigeonWatch.WebApi.Controllers;
using PigeonWatch.WebApi.Mappers;
using PigeonWatch.WebApi.Models;
using PigeonWatch.WebApi.ViewModelCreators;

namespace PigeonWatch.UnitTests;

public class AccountControllerTests
{
    private readonly IAccountRepository accountRepository = Substitute.For<IAccountRepository>();

    [Fact]
    public async Task Update_profile_returns_the_updated_profile()
    {
        Guid userId = Guid.NewGuid();
        accountRepository
            .UpdateDisplayNameAsync(userId, "Feathers", Arg.Any<CancellationToken>())
            .Returns(ProfileUpdateResult.Success(new UpdatedProfile("user@example.com", "Feathers")));
        AccountController controller = CreateController(Authenticated(userId));

        ActionResult<UpdatedProfileModel> result = await controller.UpdateProfile(
            new UpdateProfileRequestModel { DisplayName = " Feathers " },
            TestContext.Current.CancellationToken);

        UpdatedProfileModel model = Assert.IsType<UpdatedProfileModel>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("user@example.com", model.Email);
        Assert.Equal("Feathers", model.DisplayName);
    }

    [Fact]
    public async Task Update_profile_returns_400_keyed_by_code_for_a_taken_display_name()
    {
        Guid userId = Guid.NewGuid();
        accountRepository
            .UpdateDisplayNameAsync(userId, "Feathers", Arg.Any<CancellationToken>())
            .Returns(ProfileUpdateResult.Failure([new AccountError(AccountErrorCodes.DuplicateDisplayName, "taken")]));
        AccountController controller = CreateController(Authenticated(userId));

        ActionResult<UpdatedProfileModel> result = await controller.UpdateProfile(
            new UpdateProfileRequestModel { DisplayName = "Feathers" },
            TestContext.Current.CancellationToken);

        ValidationProblemDetails problem = Assert.IsType<ValidationProblemDetails>(Assert.IsType<BadRequestObjectResult>(result.Result).Value);
        Assert.Equal([AccountErrorCodes.DuplicateDisplayName], problem.Errors.Keys);
    }

    [Fact]
    public async Task Update_profile_returns_400_keyed_by_code_for_an_invalid_display_name_without_calling_the_repository()
    {
        AccountController controller = CreateController(Authenticated(Guid.NewGuid()));

        ActionResult<UpdatedProfileModel> result = await controller.UpdateProfile(
            new UpdateProfileRequestModel { DisplayName = "ab@" },
            TestContext.Current.CancellationToken);

        ValidationProblemDetails problem = Assert.IsType<ValidationProblemDetails>(Assert.IsType<BadRequestObjectResult>(result.Result).Value);
        Assert.Equal([AccountErrorCodes.DisplayNameInvalidCharacter], problem.Errors.Keys);
        await accountRepository.DidNotReceive().UpdateDisplayNameAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_profile_returns_401_when_the_name_identifier_claim_is_missing()
    {
        AccountController controller = CreateController(new ClaimsPrincipal(new ClaimsIdentity()));

        ActionResult<UpdatedProfileModel> result = await controller.UpdateProfile(
            new UpdateProfileRequestModel { DisplayName = "Feathers" },
            TestContext.Current.CancellationToken);

        Assert.IsType<UnauthorizedResult>(result.Result);
        await accountRepository.DidNotReceive().UpdateDisplayNameAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_profile_returns_401_when_the_name_identifier_claim_is_not_a_guid()
    {
        ClaimsPrincipal user = new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "not-a-guid")], "Bearer"));
        AccountController controller = CreateController(user);

        ActionResult<UpdatedProfileModel> result = await controller.UpdateProfile(
            new UpdateProfileRequestModel { DisplayName = "Feathers" },
            TestContext.Current.CancellationToken);

        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Fact]
    public async Task Update_profile_returns_401_for_an_unknown_user()
    {
        Guid unknownId = Guid.NewGuid();
        accountRepository
            .UpdateDisplayNameAsync(unknownId, "Feathers", Arg.Any<CancellationToken>())
            .Returns(ProfileUpdateResult.Failure([new AccountError(AccountErrorCodes.UserNotFound, "missing")]));
        AccountController controller = CreateController(Authenticated(unknownId));

        ActionResult<UpdatedProfileModel> result = await controller.UpdateProfile(
            new UpdateProfileRequestModel { DisplayName = "Feathers" },
            TestContext.Current.CancellationToken);

        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Fact]
    public async Task Change_password_returns_200_with_the_changed_model()
    {
        Guid userId = Guid.NewGuid();
        accountRepository
            .ChangePasswordAsync(userId, "Old1!", "New1Password!", Arg.Any<CancellationToken>())
            .Returns(PasswordChangeResult.Success());
        AccountController controller = CreateController(Authenticated(userId));

        ActionResult<PasswordChangedModel> result = await controller.ChangePassword(
            new ChangePasswordRequestModel { CurrentPassword = "Old1!", NewPassword = "New1Password!" },
            TestContext.Current.CancellationToken);

        PasswordChangedModel model = Assert.IsType<PasswordChangedModel>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.True(model.Changed);
    }

    [Fact]
    public async Task Change_password_returns_400_keyed_by_code_for_a_wrong_current_password()
    {
        Guid userId = Guid.NewGuid();
        accountRepository
            .ChangePasswordAsync(userId, "Wrong1!", "New1Password!", Arg.Any<CancellationToken>())
            .Returns(PasswordChangeResult.Failure([new AccountError(AccountErrorCodes.PasswordMismatch, "wrong")]));
        AccountController controller = CreateController(Authenticated(userId));

        ActionResult<PasswordChangedModel> result = await controller.ChangePassword(
            new ChangePasswordRequestModel { CurrentPassword = "Wrong1!", NewPassword = "New1Password!" },
            TestContext.Current.CancellationToken);

        ValidationProblemDetails problem = Assert.IsType<ValidationProblemDetails>(Assert.IsType<BadRequestObjectResult>(result.Result).Value);
        Assert.Equal([AccountErrorCodes.PasswordMismatch], problem.Errors.Keys);
    }

    [Fact]
    public async Task Change_password_returns_400_keyed_by_every_policy_code()
    {
        Guid userId = Guid.NewGuid();
        accountRepository
            .ChangePasswordAsync(userId, "Old1!", "weak", Arg.Any<CancellationToken>())
            .Returns(PasswordChangeResult.Failure(
            [
                new AccountError(AccountErrorCodes.PasswordTooShort, "short"),
                new AccountError(AccountErrorCodes.PasswordRequiresDigit, "digit")
            ]));
        AccountController controller = CreateController(Authenticated(userId));

        ActionResult<PasswordChangedModel> result = await controller.ChangePassword(
            new ChangePasswordRequestModel { CurrentPassword = "Old1!", NewPassword = "weak" },
            TestContext.Current.CancellationToken);

        ValidationProblemDetails problem = Assert.IsType<ValidationProblemDetails>(Assert.IsType<BadRequestObjectResult>(result.Result).Value);
        Assert.Equal([AccountErrorCodes.PasswordTooShort, AccountErrorCodes.PasswordRequiresDigit], problem.Errors.Keys);
    }

    [Fact]
    public async Task Change_password_returns_401_when_the_name_identifier_claim_is_missing()
    {
        AccountController controller = CreateController(new ClaimsPrincipal(new ClaimsIdentity()));

        ActionResult<PasswordChangedModel> result = await controller.ChangePassword(
            new ChangePasswordRequestModel { CurrentPassword = "Old1!", NewPassword = "New1Password!" },
            TestContext.Current.CancellationToken);

        Assert.IsType<UnauthorizedResult>(result.Result);
        await accountRepository.DidNotReceive().ChangePasswordAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Change_password_returns_401_for_an_unknown_user()
    {
        Guid unknownId = Guid.NewGuid();
        accountRepository
            .ChangePasswordAsync(unknownId, "Old1!", "New1Password!", Arg.Any<CancellationToken>())
            .Returns(PasswordChangeResult.Failure([new AccountError(AccountErrorCodes.UserNotFound, "missing")]));
        AccountController controller = CreateController(Authenticated(unknownId));

        ActionResult<PasswordChangedModel> result = await controller.ChangePassword(
            new ChangePasswordRequestModel { CurrentPassword = "Old1!", NewPassword = "New1Password!" },
            TestContext.Current.CancellationToken);

        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    private AccountController CreateController(ClaimsPrincipal user)
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

        return new(
            new AccountService(accountRepository),
            new RegisterRequestMapper(),
            new RegisteredAccountViewModelCreator(),
            new UpdatedProfileViewModelCreator(),
            new PasswordChangedViewModelCreator())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } },
            ProblemDetailsFactory = problemDetailsFactory
        };
    }

    private static ClaimsPrincipal Authenticated(Guid userId)
    {
        return new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Bearer"));
    }
}

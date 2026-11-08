using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PigeonWatch.BusinessLogic.Services;
using PigeonWatch.BusinessObjects;
using PigeonWatch.WebApi.Controllers;
using PigeonWatch.WebApi.Models;
using PigeonWatch.WebApi.ViewModelCreators;

namespace PigeonWatch.UnitTests;

public class ThreadsControllerTests
{
    private readonly ISightingThreadService service = Substitute.For<ISightingThreadService>();
    private readonly ILogger<ThreadsController> logger = Substitute.For<ILogger<ThreadsController>>();
    private readonly CancellationToken cancellationToken = TestContext.Current.CancellationToken;
    private readonly Guid userId = Guid.NewGuid();

    [Fact]
    public async Task List_returns_the_callers_threads_with_the_counterpart_per_role_without_caching()
    {
        Guid otherUserId = Guid.NewGuid();
        SightingThread owned = new(Guid.NewGuid(), Guid.NewGuid(), "Grey", userId, "Alice", otherUserId, "Bob", DateTime.UtcNow, true);
        SightingThread joined = new(Guid.NewGuid(), Guid.NewGuid(), null, otherUserId, "Carol", userId, "Alice", DateTime.UtcNow, false);
        service.ListForUserAsync(userId, 1, Arg.Any<CancellationToken>())
            .Returns(SightingThreadPageResult.Success(new SightingThreadPage([owned, joined], 2)));
        ThreadsController controller = CreateController(Authenticated(userId));

        ActionResult<SightingThreadPageModel> result = await controller.List(null, cancellationToken);

        SightingThreadPageModel model = Assert.IsType<SightingThreadPageModel>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal([true, false], model.Items.Select(item => item.IsOwner));
        Assert.Equal(["Bob", "Carol"], model.Items.Select(item => item.CounterpartDisplayName));
        Assert.Equal([owned.SightingId, joined.SightingId], model.Items.Select(item => item.SightingId));
        Assert.Equal([true, false], model.Items.Select(item => item.HasUnread));
        Assert.Equal(2, model.TotalCount);
        Assert.Equal("private, no-store", controller.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task List_returns_400_logged_at_warning_for_an_invalid_page()
    {
        service.ListForUserAsync(userId, 0, Arg.Any<CancellationToken>())
            .Returns(SightingThreadPageResult.Failure([new SightingError(SightingMessageErrorCodes.InvalidPage, "page")]));
        ThreadsController controller = CreateController(Authenticated(userId));

        ActionResult<SightingThreadPageModel> result = await controller.List(0, cancellationToken);

        ValidationProblemDetails problem = Assert.IsType<ValidationProblemDetails>(Assert.IsType<BadRequestObjectResult>(result.Result).Value);
        Assert.Equal([SightingMessageErrorCodes.InvalidPage], problem.Errors.Keys);
        Assert.Equal(LogLevel.Warning, Assert.Single(logger.ReceivedCalls(), call => call.GetMethodInfo().Name == nameof(ILogger.Log)).GetArguments()[0]);
    }

    [Fact]
    public async Task List_returns_401_when_the_caller_no_longer_exists()
    {
        service.ListForUserAsync(userId, 1, Arg.Any<CancellationToken>())
            .Returns(SightingThreadPageResult.Failure([new SightingError(SightingMessageErrorCodes.SenderNotFound, "sender")]));
        ThreadsController controller = CreateController(Authenticated(userId));

        ActionResult<SightingThreadPageModel> result = await controller.List(null, cancellationToken);

        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Fact]
    public async Task List_returns_401_for_a_missing_name_identifier_claim()
    {
        ThreadsController controller = CreateController(new ClaimsPrincipal(new ClaimsIdentity()));

        ActionResult<SightingThreadPageModel> result = await controller.List(null, cancellationToken);

        Assert.IsType<UnauthorizedResult>(result.Result);
        await service.DidNotReceive().ListForUserAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unread_count_returns_the_callers_count_without_caching()
    {
        service.CountUnreadMessagesAsync(userId, Arg.Any<CancellationToken>()).Returns(4);
        ThreadsController controller = CreateController(Authenticated(userId));

        ActionResult<UnreadMessageCountModel> result = await controller.UnreadCount(cancellationToken);

        UnreadMessageCountModel model = Assert.IsType<UnreadMessageCountModel>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(4, model.Count);
        Assert.Equal("private, no-store", controller.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task Unread_count_returns_401_for_a_missing_name_identifier_claim()
    {
        ThreadsController controller = CreateController(new ClaimsPrincipal(new ClaimsIdentity()));

        ActionResult<UnreadMessageCountModel> result = await controller.UnreadCount(cancellationToken);

        Assert.IsType<UnauthorizedResult>(result.Result);
        await service.DidNotReceive().CountUnreadMessagesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    private ThreadsController CreateController(ClaimsPrincipal user)
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

        return new(service, new SightingThreadViewModelCreator(), logger)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } },
            ProblemDetailsFactory = problemDetailsFactory
        };
    }

    private static ClaimsPrincipal Authenticated(Guid id)
    {
        return new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.ToString())], "Bearer"));
    }
}

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
using PigeonWatch.WebApi.Models;
using PigeonWatch.WebApi.ViewModelCreators;

namespace PigeonWatch.UnitTests;

public class SightingThreadsControllerTests
{
    private static readonly DateTime createdAt = new(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc);

    private readonly ISightingThreadService service = Substitute.For<ISightingThreadService>();
    private readonly ILogger<SightingThreadsController> logger = Substitute.For<ILogger<SightingThreadsController>>();
    private readonly CancellationToken cancellationToken = TestContext.Current.CancellationToken;
    private readonly Guid sightingId = Guid.NewGuid();
    private readonly Guid threadId = Guid.NewGuid();
    private readonly Guid ownerId = Guid.NewGuid();
    private readonly Guid userId = Guid.NewGuid();

    [Fact]
    public async Task List_returns_the_threads_for_the_caller_without_caching()
    {
        service.ListForSightingAsync(sightingId, ownerId, 1, Arg.Any<CancellationToken>())
            .Returns(SightingThreadPageResult.Success(new SightingThreadPage([Thread()], 1)));
        SightingThreadsController controller = CreateController(Authenticated(ownerId));

        ActionResult<SightingThreadPageModel> result = await controller.List(sightingId, null, cancellationToken);

        SightingThreadPageModel model = Assert.IsType<SightingThreadPageModel>(Assert.IsType<OkObjectResult>(result.Result).Value);
        SightingThreadModel thread = Assert.Single(model.Items);
        Assert.True(thread.IsOwner);
        Assert.Equal("Bob", thread.CounterpartDisplayName);
        Assert.Equal(1, model.TotalCount);
        Assert.Equal("private, no-store", controller.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task List_returns_404_logged_at_information_for_an_unknown_sighting()
    {
        service.ListForSightingAsync(sightingId, userId, 1, Arg.Any<CancellationToken>())
            .Returns(SightingThreadPageResult.Failure([Error(SightingMessageErrorCodes.SightingNotFound)]));
        SightingThreadsController controller = CreateController(Authenticated(userId));

        ActionResult<SightingThreadPageModel> result = await controller.List(sightingId, 1, cancellationToken);

        Assert.IsType<NotFoundResult>(result.Result);
        LogCall call = Assert.Single(LogCalls());
        Assert.Equal(LogLevel.Information, call.Level);
        Assert.Equal(sightingId, call.State["SightingId"]);
    }

    [Fact]
    public async Task List_returns_400_logged_at_warning_for_an_invalid_page()
    {
        service.ListForSightingAsync(sightingId, userId, 0, Arg.Any<CancellationToken>())
            .Returns(SightingThreadPageResult.Failure([Error(SightingMessageErrorCodes.InvalidPage)]));
        SightingThreadsController controller = CreateController(Authenticated(userId));

        ActionResult<SightingThreadPageModel> result = await controller.List(sightingId, 0, cancellationToken);

        ValidationProblemDetails problem = Assert.IsType<ValidationProblemDetails>(Assert.IsType<BadRequestObjectResult>(result.Result).Value);
        Assert.Equal([SightingMessageErrorCodes.InvalidPage], problem.Errors.Keys);
        LogCall call = Assert.Single(LogCalls());
        Assert.Equal(LogLevel.Warning, call.Level);
        Assert.Equal(SightingMessageErrorCodes.InvalidPage, call.State["ErrorCode"]);
    }

    [Fact]
    public async Task List_returns_401_for_a_missing_name_identifier_claim()
    {
        SightingThreadsController controller = CreateController(new ClaimsPrincipal(new ClaimsIdentity()));

        ActionResult<SightingThreadPageModel> result = await controller.List(sightingId, null, cancellationToken);

        Assert.IsType<UnauthorizedResult>(result.Result);
        await service.DidNotReceive().ListForSightingAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Start_returns_201_pointing_at_the_thread_messages()
    {
        SightingThread thread = Thread();
        SightingMessage message = Message(userId);
        service.StartAsync(sightingId, userId, "Hello", Arg.Any<CancellationToken>()).Returns(SightingThreadResult.Success(thread, message));
        SightingThreadsController controller = CreateController(Authenticated(userId));

        ActionResult<SightingThreadStartModel> result = await controller.Start(sightingId, Request("Hello"), cancellationToken);

        CreatedAtActionResult created = Assert.IsType<CreatedAtActionResult>(result.Result);
        SightingThreadStartModel model = Assert.IsType<SightingThreadStartModel>(created.Value);
        Assert.Equal(nameof(SightingThreadsController.ListMessages), created.ActionName);
        Assert.Equal(sightingId, created.RouteValues!["sightingId"]);
        Assert.Equal(thread.Id, created.RouteValues["threadId"]);
        Assert.Equal(thread.Id, model.Thread.Id);
        Assert.False(model.Thread.IsOwner);
        Assert.Equal("Alice", model.Thread.CounterpartDisplayName);
        Assert.Equal(message.Id, model.Message.Id);
        Assert.True(model.Message.IsMine);
        Assert.Empty(LogCalls());
    }

    [Fact]
    public async Task Start_returns_400_logged_at_warning_when_the_owner_contacts_their_own_sighting()
    {
        service.StartAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(SightingThreadResult.Failure([Error(SightingMessageErrorCodes.OwnSightingContact)]));
        SightingThreadsController controller = CreateController(Authenticated(ownerId));

        ActionResult<SightingThreadStartModel> result = await controller.Start(sightingId, Request("Hello"), cancellationToken);

        ValidationProblemDetails problem = Assert.IsType<ValidationProblemDetails>(Assert.IsType<BadRequestObjectResult>(result.Result).Value);
        Assert.Equal([SightingMessageErrorCodes.OwnSightingContact], problem.Errors.Keys);
        LogCall call = Assert.Single(LogCalls());
        Assert.Equal(LogLevel.Warning, call.Level);
        Assert.Equal(SightingMessageErrorCodes.OwnSightingContact, call.State["ErrorCode"]);
    }

    [Theory]
    [InlineData(SightingMessageErrorCodes.MessageTextRequired)]
    [InlineData(SightingMessageErrorCodes.MessageTextTooLong)]
    public async Task Start_returns_400_without_logging_for_invalid_text(string code)
    {
        service.StartAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(SightingThreadResult.Failure([Error(code)]));
        SightingThreadsController controller = CreateController(Authenticated(userId));

        ActionResult<SightingThreadStartModel> result = await controller.Start(sightingId, Request(" "), cancellationToken);

        ValidationProblemDetails problem = Assert.IsType<ValidationProblemDetails>(Assert.IsType<BadRequestObjectResult>(result.Result).Value);
        Assert.Equal([code], problem.Errors.Keys);
        Assert.False(problem.Extensions.ContainsKey("retryAfterSeconds"));
        Assert.Empty(LogCalls());
    }

    [Fact]
    public async Task Start_returns_404_for_an_unknown_sighting()
    {
        service.StartAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(SightingThreadResult.Failure([Error(SightingMessageErrorCodes.SightingNotFound)]));
        SightingThreadsController controller = CreateController(Authenticated(userId));

        ActionResult<SightingThreadStartModel> result = await controller.Start(sightingId, Request("Hello"), cancellationToken);

        Assert.IsType<NotFoundResult>(result.Result);
        Assert.Equal(LogLevel.Information, Assert.Single(LogCalls()).Level);
    }

    [Fact]
    public async Task Start_returns_401_logged_at_warning_when_the_sender_no_longer_exists()
    {
        service.StartAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(SightingThreadResult.Failure([Error(SightingMessageErrorCodes.SenderNotFound)]));
        SightingThreadsController controller = CreateController(Authenticated(userId));

        ActionResult<SightingThreadStartModel> result = await controller.Start(sightingId, Request("Hello"), cancellationToken);

        Assert.IsType<UnauthorizedResult>(result.Result);
        Assert.Equal(LogLevel.Warning, Assert.Single(LogCalls()).Level);
    }

    [Fact]
    public async Task Start_returns_401_for_a_missing_name_identifier_claim()
    {
        SightingThreadsController controller = CreateController(new ClaimsPrincipal(new ClaimsIdentity()));

        ActionResult<SightingThreadStartModel> result = await controller.Start(sightingId, Request("Hello"), cancellationToken);

        Assert.IsType<UnauthorizedResult>(result.Result);
        await service.DidNotReceive().StartAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task List_messages_returns_the_page_with_is_mine_without_caching()
    {
        SightingMessage mine = Message(userId);
        SightingMessage other = Message(ownerId);
        service.ListMessagesAsync(sightingId, threadId, userId, 1, Arg.Any<CancellationToken>())
            .Returns(SightingMessagePageResult.Success(new SightingMessagePage([mine, other], 2)));
        SightingThreadsController controller = CreateController(Authenticated(userId));

        ActionResult<SightingMessagePageModel> result = await controller.ListMessages(sightingId, threadId, null, cancellationToken);

        SightingMessagePageModel model = Assert.IsType<SightingMessagePageModel>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal([true, false], model.Items.Select(item => item.IsMine));
        Assert.Equal(2, model.TotalCount);
        Assert.Equal("private, no-store", controller.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task List_messages_returns_404_logged_at_information_for_a_thread_the_caller_cannot_see()
    {
        service.ListMessagesAsync(sightingId, threadId, userId, 1, Arg.Any<CancellationToken>())
            .Returns(SightingMessagePageResult.Failure([Error(SightingMessageErrorCodes.ThreadNotFound)]));
        SightingThreadsController controller = CreateController(Authenticated(userId));

        ActionResult<SightingMessagePageModel> result = await controller.ListMessages(sightingId, threadId, 1, cancellationToken);

        Assert.IsType<NotFoundResult>(result.Result);
        LogCall call = Assert.Single(LogCalls());
        Assert.Equal(LogLevel.Information, call.Level);
        Assert.Equal(threadId, call.State["ThreadId"]);
        Assert.Equal(sightingId, call.State["SightingId"]);
    }

    [Fact]
    public async Task Reply_returns_201_with_the_message()
    {
        SightingMessage message = Message(userId);
        service.ReplyAsync(sightingId, threadId, userId, "Thanks", Arg.Any<CancellationToken>())
            .Returns(SightingThreadResult.Success(Thread(), message));
        SightingThreadsController controller = CreateController(Authenticated(userId));

        ActionResult<SightingMessageModel> result = await controller.Reply(sightingId, threadId, Request("Thanks"), cancellationToken);

        CreatedAtActionResult created = Assert.IsType<CreatedAtActionResult>(result.Result);
        SightingMessageModel model = Assert.IsType<SightingMessageModel>(created.Value);
        Assert.Equal(nameof(SightingThreadsController.ListMessages), created.ActionName);
        Assert.Equal(threadId, created.RouteValues!["threadId"]);
        Assert.Equal(message.Id, model.Id);
        Assert.True(model.IsMine);
    }

    [Fact]
    public async Task Reply_returns_404_not_403_for_a_non_participant()
    {
        service.ReplyAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(SightingThreadResult.Failure([Error(SightingMessageErrorCodes.ThreadNotFound)]));
        SightingThreadsController controller = CreateController(Authenticated(userId));

        ActionResult<SightingMessageModel> result = await controller.Reply(sightingId, threadId, Request("Hello"), cancellationToken);

        Assert.IsType<NotFoundResult>(result.Result);
        Assert.Equal(LogLevel.Information, Assert.Single(LogCalls()).Level);
    }

    [Fact]
    public async Task Reply_returns_401_when_the_sender_no_longer_exists()
    {
        service.ReplyAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(SightingThreadResult.Failure([Error(SightingMessageErrorCodes.SenderNotFound)]));
        SightingThreadsController controller = CreateController(Authenticated(userId));

        ActionResult<SightingMessageModel> result = await controller.Reply(sightingId, threadId, Request("Hello"), cancellationToken);

        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Fact]
    public async Task List_carries_has_unread_for_the_caller()
    {
        service.ListForSightingAsync(sightingId, ownerId, 1, Arg.Any<CancellationToken>())
            .Returns(SightingThreadPageResult.Success(new SightingThreadPage([Thread() with { HasUnread = true }, Thread()], 2)));
        SightingThreadsController controller = CreateController(Authenticated(ownerId));

        ActionResult<SightingThreadPageModel> result = await controller.List(sightingId, null, cancellationToken);

        SightingThreadPageModel model = Assert.IsType<SightingThreadPageModel>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal([true, false], model.Items.Select(item => item.HasUnread));
    }

    [Fact]
    public async Task Mark_read_returns_204_without_logging()
    {
        service.MarkReadAsync(sightingId, threadId, userId, Arg.Any<CancellationToken>()).Returns(SightingThreadReadResult.Success());
        SightingThreadsController controller = CreateController(Authenticated(userId));

        ActionResult<SightingThreadReadModel> result = await controller.MarkRead(sightingId, threadId, cancellationToken);

        Assert.IsType<NoContentResult>(result.Result);
        Assert.Empty(LogCalls());
    }

    [Theory]
    [InlineData(SightingMessageErrorCodes.ThreadNotFound)]
    [InlineData(SightingMessageErrorCodes.SightingNotFound)]
    public async Task Mark_read_returns_404_logged_at_information_for_a_thread_the_caller_cannot_see(string code)
    {
        service.MarkReadAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(SightingThreadReadResult.Failure([Error(code)]));
        SightingThreadsController controller = CreateController(Authenticated(userId));

        ActionResult<SightingThreadReadModel> result = await controller.MarkRead(sightingId, threadId, cancellationToken);

        Assert.IsType<NotFoundResult>(result.Result);
        LogCall call = Assert.Single(LogCalls());
        Assert.Equal(LogLevel.Information, call.Level);
        Assert.Equal(sightingId, call.State["SightingId"]);
    }

    [Fact]
    public async Task Mark_read_returns_401_for_a_missing_name_identifier_claim()
    {
        SightingThreadsController controller = CreateController(new ClaimsPrincipal(new ClaimsIdentity()));

        ActionResult<SightingThreadReadModel> result = await controller.MarkRead(sightingId, threadId, cancellationToken);

        Assert.IsType<UnauthorizedResult>(result.Result);
        await service.DidNotReceive().MarkReadAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    private SightingThreadsController CreateController(ClaimsPrincipal user)
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

    private SightingThread Thread()
    {
        return new(Guid.NewGuid(), sightingId, "Grey", ownerId, "Alice", userId, "Bob", createdAt, false);
    }

    private SightingMessage Message(Guid senderId)
    {
        return new(Guid.NewGuid(), threadId, senderId, senderId == ownerId ? "Alice" : "Bob", "Hello", createdAt);
    }

    private static SightingError Error(string code)
    {
        return new SightingError(code, "error");
    }

    private static SightingMessageRequestModel Request(string text)
    {
        return new() { Text = text };
    }

    private static ClaimsPrincipal Authenticated(Guid id)
    {
        return new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.ToString())], "Bearer"));
    }

    private List<LogCall> LogCalls()
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

        return new LogCall((LogLevel)arguments[0]!, state);
    }

    private sealed record LogCall(LogLevel Level, Dictionary<string, object?> State);
}

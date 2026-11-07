using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PigeonWatch.BusinessLogic.Services;
using PigeonWatch.BusinessObjects;
using PigeonWatch.WebApi.Controllers;
using PigeonWatch.WebApi.Models;
using PigeonWatch.WebApi.ViewModelCreators;

namespace PigeonWatch.UnitTests;

public class SightingCommentsControllerTests
{
    private readonly ISightingCommentService service = Substitute.For<ISightingCommentService>();
    private readonly CancellationToken cancellationToken = TestContext.Current.CancellationToken;
    private readonly Guid sightingId = Guid.NewGuid();
    private readonly Guid userId = Guid.NewGuid();

    [Fact]
    public async Task List_returns_the_page_with_is_mine_for_the_caller_without_caching()
    {
        SightingComment mine = Comment(userId);
        SightingComment other = Comment(Guid.NewGuid());
        service.ListAsync(sightingId, 1, Arg.Any<CancellationToken>()).Returns(SightingCommentPageResult.Success(new SightingCommentPage([mine, other], 2)));
        SightingCommentsController controller = CreateController(Authenticated(userId));

        ActionResult<SightingCommentPageModel> result = await controller.List(sightingId, null, cancellationToken);

        SightingCommentPageModel model = Assert.IsType<SightingCommentPageModel>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal([true, false], model.Items.Select(item => item.IsMine));
        Assert.Equal(2, model.TotalCount);
        Assert.Equal("private, no-store", controller.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task List_returns_400_for_an_invalid_page()
    {
        service.ListAsync(sightingId, 0, Arg.Any<CancellationToken>())
            .Returns(SightingCommentPageResult.Failure([new SightingError(SightingCommentErrorCodes.InvalidPage, "page")]));
        SightingCommentsController controller = CreateController(Authenticated(userId));

        ActionResult<SightingCommentPageModel> result = await controller.List(sightingId, 0, cancellationToken);

        ValidationProblemDetails problem = Assert.IsType<ValidationProblemDetails>(Assert.IsType<BadRequestObjectResult>(result.Result).Value);
        Assert.Equal([SightingCommentErrorCodes.InvalidPage], problem.Errors.Keys);
    }

    [Fact]
    public async Task List_returns_404_for_an_unknown_sighting()
    {
        service.ListAsync(sightingId, 1, Arg.Any<CancellationToken>())
            .Returns(SightingCommentPageResult.Failure([new SightingError(SightingCommentErrorCodes.SightingNotFound, "missing")]));
        SightingCommentsController controller = CreateController(Authenticated(userId));

        ActionResult<SightingCommentPageModel> result = await controller.List(sightingId, 1, cancellationToken);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Create_returns_201_pointing_at_the_list()
    {
        SightingComment created = Comment(userId);
        service.CreateAsync(sightingId, userId, "Hello", Arg.Any<CancellationToken>()).Returns(SightingCommentResult.Success(created));
        SightingCommentsController controller = CreateController(Authenticated(userId));

        ActionResult<SightingCommentModel> result = await controller.Create(sightingId, Request("Hello"), cancellationToken);

        CreatedAtActionResult createdResult = Assert.IsType<CreatedAtActionResult>(result.Result);
        SightingCommentModel model = Assert.IsType<SightingCommentModel>(createdResult.Value);
        Assert.Equal(nameof(SightingCommentsController.List), createdResult.ActionName);
        Assert.Equal(sightingId, createdResult.RouteValues!["sightingId"]);
        Assert.Equal(created.Id, model.Id);
        Assert.True(model.IsMine);
    }

    [Fact]
    public async Task Create_returns_400_with_retry_after_while_the_cooldown_is_active()
    {
        service.CreateAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(SightingCommentResult.CooldownFailure(new SightingError(SightingCommentErrorCodes.CooldownActive, "wait"), 42));
        SightingCommentsController controller = CreateController(Authenticated(userId));

        ActionResult<SightingCommentModel> result = await controller.Create(sightingId, Request("Hello"), cancellationToken);

        ValidationProblemDetails problem = Assert.IsType<ValidationProblemDetails>(Assert.IsType<BadRequestObjectResult>(result.Result).Value);
        Assert.Equal([SightingCommentErrorCodes.CooldownActive], problem.Errors.Keys);
        Assert.Equal(42, problem.Extensions["retryAfterSeconds"]);
        Assert.Equal("42", controller.Response.Headers.RetryAfter.ToString());
    }

    [Fact]
    public async Task Create_returns_400_for_missing_text_without_retry_after()
    {
        service.CreateAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(SightingCommentResult.Failure([new SightingError(SightingCommentErrorCodes.CommentTextRequired, "text")]));
        SightingCommentsController controller = CreateController(Authenticated(userId));

        ActionResult<SightingCommentModel> result = await controller.Create(sightingId, Request(" "), cancellationToken);

        ValidationProblemDetails problem = Assert.IsType<ValidationProblemDetails>(Assert.IsType<BadRequestObjectResult>(result.Result).Value);
        Assert.Equal([SightingCommentErrorCodes.CommentTextRequired], problem.Errors.Keys);
        Assert.False(problem.Extensions.ContainsKey("retryAfterSeconds"));
    }

    [Fact]
    public async Task Create_returns_404_for_an_unknown_sighting()
    {
        service.CreateAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(SightingCommentResult.Failure([new SightingError(SightingCommentErrorCodes.SightingNotFound, "missing")]));
        SightingCommentsController controller = CreateController(Authenticated(userId));

        ActionResult<SightingCommentModel> result = await controller.Create(sightingId, Request("Hello"), cancellationToken);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Create_returns_401_when_the_author_no_longer_exists()
    {
        service.CreateAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(SightingCommentResult.Failure([new SightingError(SightingCommentErrorCodes.AuthorNotFound, "missing")]));
        SightingCommentsController controller = CreateController(Authenticated(userId));

        ActionResult<SightingCommentModel> result = await controller.Create(sightingId, Request("Hello"), cancellationToken);

        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Fact]
    public async Task Create_returns_401_for_a_missing_name_identifier_claim()
    {
        SightingCommentsController controller = CreateController(new ClaimsPrincipal(new ClaimsIdentity()));

        ActionResult<SightingCommentModel> result = await controller.Create(sightingId, Request("Hello"), cancellationToken);

        Assert.IsType<UnauthorizedResult>(result.Result);
        await service.DidNotReceive().CreateAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_returns_200_with_the_edited_comment()
    {
        Guid commentId = Guid.NewGuid();
        SightingComment edited = Comment(userId) with { Id = commentId, Text = "Edited", EditedAt = DateTime.UtcNow };
        service.UpdateAsync(sightingId, commentId, userId, "Edited", Arg.Any<CancellationToken>()).Returns(SightingCommentResult.Success(edited));
        SightingCommentsController controller = CreateController(Authenticated(userId));

        ActionResult<SightingCommentModel> result = await controller.Update(sightingId, commentId, Request("Edited"), cancellationToken);

        SightingCommentModel model = Assert.IsType<SightingCommentModel>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("Edited", model.Text);
        Assert.NotNull(model.EditedAt);
    }

    [Fact]
    public async Task Update_returns_403_for_another_users_comment()
    {
        service.UpdateAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(SightingCommentResult.Failure([new SightingError(SightingCommentErrorCodes.NotCommentAuthor, "forbidden")]));
        SightingCommentsController controller = CreateController(Authenticated(userId));

        ActionResult<SightingCommentModel> result = await controller.Update(sightingId, Guid.NewGuid(), Request("Edited"), cancellationToken);

        StatusCodeResult status = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, status.StatusCode);
    }

    [Fact]
    public async Task Update_returns_404_for_a_missing_or_deleted_comment()
    {
        service.UpdateAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(SightingCommentResult.Failure([new SightingError(SightingCommentErrorCodes.CommentNotFound, "missing")]));
        SightingCommentsController controller = CreateController(Authenticated(userId));

        ActionResult<SightingCommentModel> result = await controller.Update(sightingId, Guid.NewGuid(), Request("Edited"), cancellationToken);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Delete_returns_200_with_the_placeholder()
    {
        Guid commentId = Guid.NewGuid();
        SightingComment deleted = Comment(userId) with { Id = commentId, DeletedAt = DateTime.UtcNow };
        service.DeleteAsync(sightingId, commentId, userId, Arg.Any<CancellationToken>()).Returns(SightingCommentResult.Success(deleted));
        SightingCommentsController controller = CreateController(Authenticated(userId));

        ActionResult<SightingCommentModel> result = await controller.Delete(sightingId, commentId, cancellationToken);

        SightingCommentModel model = Assert.IsType<SightingCommentModel>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.True(model.IsDeleted);
        Assert.Null(model.Text);
        Assert.Null(model.AuthorDisplayName);
    }

    [Fact]
    public async Task Delete_maps_not_found_and_not_author()
    {
        Guid missingId = Guid.NewGuid();
        Guid foreignId = Guid.NewGuid();
        service.DeleteAsync(sightingId, missingId, userId, Arg.Any<CancellationToken>())
            .Returns(SightingCommentResult.Failure([new SightingError(SightingCommentErrorCodes.CommentNotFound, "missing")]));
        service.DeleteAsync(sightingId, foreignId, userId, Arg.Any<CancellationToken>())
            .Returns(SightingCommentResult.Failure([new SightingError(SightingCommentErrorCodes.NotCommentAuthor, "forbidden")]));
        SightingCommentsController controller = CreateController(Authenticated(userId));

        ActionResult<SightingCommentModel> missing = await controller.Delete(sightingId, missingId, cancellationToken);
        ActionResult<SightingCommentModel> foreign = await controller.Delete(sightingId, foreignId, cancellationToken);

        Assert.IsType<NotFoundResult>(missing.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<StatusCodeResult>(foreign.Result).StatusCode);
    }

    private SightingCommentsController CreateController(ClaimsPrincipal user)
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

        return new(service, new SightingCommentViewModelCreator(), NullLogger<SightingCommentsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } },
            ProblemDetailsFactory = problemDetailsFactory
        };
    }

    private SightingComment Comment(Guid authorId)
    {
        return new(Guid.NewGuid(), sightingId, authorId, "Pidgey", "Hello", new DateTime(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc), null, null);
    }

    private static SightingCommentRequestModel Request(string text)
    {
        return new() { Text = text };
    }

    private static ClaimsPrincipal Authenticated(Guid id)
    {
        return new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.ToString())], "Bearer"));
    }
}

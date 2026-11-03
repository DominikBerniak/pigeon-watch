using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.Core;
using PigeonWatch.BusinessObjects;
using PigeonWatch.Data.Diagnostics;

namespace PigeonWatch.UnitTests;

public class PhotoStorageUnavailableExceptionHandlerTests
{
    private const string email = "reporter@example.com";

    private readonly IProblemDetailsService problemDetailsService = Substitute.For<IProblemDetailsService>();
    private readonly ILogger<PhotoStorageUnavailableExceptionHandler> logger = Substitute.For<ILogger<PhotoStorageUnavailableExceptionHandler>>();

    public static TheoryData<Exception> StorageExceptions => new()
    {
        new PhotoStorageUnavailableException("Photo storage could not save blob.", new TimeoutException("Timed out.")),
        new InvalidOperationException("Wrapped.", new PhotoStorageUnavailableException("Photo storage could not save blob.", new TimeoutException("Timed out.")))
    };

    [Theory]
    [MemberData(nameof(StorageExceptions))]
    public async Task Photo_storage_unavailability_maps_to_503_with_retry_after_and_error_key(Exception exception)
    {
        Guid userId = Guid.NewGuid();
        DefaultHttpContext httpContext = CreateHttpContext(userId);
        ProblemDetailsContext? written = null;
        problemDetailsService.TryWriteAsync(Arg.Do<ProblemDetailsContext>(context => written = context));

        bool handled = await CreateHandler().TryHandleAsync(httpContext, exception, TestContext.Current.CancellationToken);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, httpContext.Response.StatusCode);
        Assert.Equal("10", httpContext.Response.Headers.RetryAfter.ToString());
        Assert.NotNull(written);
        Assert.Same(httpContext, written.HttpContext);
        ValidationProblemDetails problem = Assert.IsType<ValidationProblemDetails>(written.ProblemDetails);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, problem.Status);
        Assert.True(problem.Errors.ContainsKey(SightingErrorCodes.PhotoStorageUnavailable));
        Assert.Equal("PhotoStorageUnavailable", SightingErrorCodes.PhotoStorageUnavailable);
    }

    [Fact]
    public async Task Photo_storage_unavailability_is_logged_at_error_with_cause_user_and_trace_but_no_email()
    {
        Guid userId = Guid.NewGuid();
        DefaultHttpContext httpContext = CreateHttpContext(userId);
        PhotoStorageUnavailableException exception = new("Photo storage could not save blob.", new TimeoutException("Timed out."));
        string expectedTraceId = Activity.Current?.TraceId.ToString() ?? httpContext.TraceIdentifier;

        await CreateHandler().TryHandleAsync(httpContext, exception, TestContext.Current.CancellationToken);

        ICall logCall = Assert.Single(LogCalls());
        Assert.Equal(LogLevel.Error, logCall.GetArguments()[0]);
        Assert.Same(exception, logCall.GetArguments()[3]);
        Assert.IsType<TimeoutException>(exception.InnerException);
        Assert.Equal(userId.ToString(), State(logCall, "UserId"));
        Assert.Equal(expectedTraceId, State(logCall, "TraceId"));
        Assert.Equal("10", State(logCall, "RetryAfterSeconds"));
        IReadOnlyList<KeyValuePair<string, object?>> state = (IReadOnlyList<KeyValuePair<string, object?>>)logCall.GetArguments()[2]!;
        Assert.DoesNotContain(state, pair => pair.Value?.ToString()?.Contains(email, StringComparison.OrdinalIgnoreCase) == true);
        Assert.DoesNotContain(email, state.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Other_exceptions_are_left_unhandled()
    {
        DefaultHttpContext httpContext = CreateHttpContext(Guid.NewGuid());

        bool handled = await CreateHandler().TryHandleAsync(httpContext, new InvalidOperationException("Not a storage outage."), TestContext.Current.CancellationToken);

        Assert.False(handled);
        Assert.Equal(StatusCodes.Status200OK, httpContext.Response.StatusCode);
        Assert.False(httpContext.Response.Headers.ContainsKey("Retry-After"));
        await problemDetailsService.DidNotReceive().TryWriteAsync(Arg.Any<ProblemDetailsContext>());
        Assert.Empty(LogCalls());
    }

    private PhotoStorageUnavailableExceptionHandler CreateHandler()
    {
        return new(problemDetailsService, logger);
    }

    private static DefaultHttpContext CreateHttpContext(Guid userId)
    {
        DefaultHttpContext httpContext = new()
        {
            TraceIdentifier = "trace-photo-storage",
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim(ClaimTypes.Email, email)],
                "Test"))
        };

        return httpContext;
    }

    private static object? State(ICall logCall, string key)
    {
        IReadOnlyList<KeyValuePair<string, object?>> state = (IReadOnlyList<KeyValuePair<string, object?>>)logCall.GetArguments()[2]!;

        return state.Single(pair => pair.Key == key).Value;
    }

    private IEnumerable<ICall> LogCalls()
    {
        return logger.ReceivedCalls().Where(call => call.GetMethodInfo().Name == nameof(ILogger.Log));
    }
}

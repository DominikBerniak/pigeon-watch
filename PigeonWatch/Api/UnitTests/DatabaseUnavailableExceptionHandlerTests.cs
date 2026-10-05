using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.Core;
using PigeonWatch.Data.Diagnostics;

namespace PigeonWatch.UnitTests;

public class DatabaseUnavailableExceptionHandlerTests
{
    private readonly IProblemDetailsService problemDetailsService = Substitute.For<IProblemDetailsService>();
    private readonly ILogger<DatabaseUnavailableExceptionHandler> logger = Substitute.For<ILogger<DatabaseUnavailableExceptionHandler>>();

    public static TheoryData<Exception> UnavailableExceptions => new()
    {
        new RetryLimitExceededException("Retry limit exceeded.", CreateSqlException(40613)),
        new InvalidOperationException("Wrapped.", CreateSqlException(40613)),
        new InvalidOperationException("Wrapped.", CreateSqlException(53)),
        new DbUpdateException("Wrapped twice.", new InvalidOperationException("Wrapped.", CreateSqlException(53)))
    };

    public static TheoryData<Exception> OtherExceptions => new()
    {
        new InvalidOperationException("Not a database outage."),
        new DbUpdateException("Duplicate key.", CreateSqlException(2627)),
        CreateSqlException(2627)
    };

    [Theory]
    [MemberData(nameof(UnavailableExceptions))]
    public async Task Database_unavailability_maps_to_503_with_retry_after(Exception exception)
    {
        DefaultHttpContext httpContext = new();

        bool handled = await CreateHandler().TryHandleAsync(httpContext, exception, TestContext.Current.CancellationToken);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, httpContext.Response.StatusCode);
        Assert.Equal("10", httpContext.Response.Headers.RetryAfter.ToString());
        await problemDetailsService.Received(1).TryWriteAsync(Arg.Is<ProblemDetailsContext>(context =>
            context.HttpContext == httpContext
            && context.ProblemDetails.Status == StatusCodes.Status503ServiceUnavailable
            && context.ProblemDetails.Title == "Service warming up"));
        ICall logCall = Assert.Single(LogCalls());
        Assert.Equal(LogLevel.Warning, logCall.GetArguments()[0]);
        Assert.Same(exception, logCall.GetArguments()[3]);
    }

    [Theory]
    [MemberData(nameof(OtherExceptions))]
    public async Task Other_exceptions_are_left_unhandled(Exception exception)
    {
        DefaultHttpContext httpContext = new();

        bool handled = await CreateHandler().TryHandleAsync(httpContext, exception, TestContext.Current.CancellationToken);

        Assert.False(handled);
        Assert.Equal(StatusCodes.Status200OK, httpContext.Response.StatusCode);
        Assert.False(httpContext.Response.Headers.ContainsKey("Retry-After"));
        await problemDetailsService.DidNotReceive().TryWriteAsync(Arg.Any<ProblemDetailsContext>());
        Assert.Empty(LogCalls());
    }

    private DatabaseUnavailableExceptionHandler CreateHandler()
    {
        return new(problemDetailsService, logger);
    }

    private IEnumerable<ICall> LogCalls()
    {
        return logger.ReceivedCalls().Where(call => call.GetMethodInfo().Name == nameof(ILogger.Log));
    }

    private static SqlException CreateSqlException(int number)
    {
        ConstructorInfo errorConstructor = typeof(SqlError)
            .GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
            .OrderBy(constructor => constructor.GetParameters().Length)
            .First();
        object?[] errorArguments = errorConstructor.GetParameters()
            .Select(parameter => parameter.Name == "infoNumber" ? number : DefaultArgument(parameter.ParameterType))
            .ToArray();
        SqlError error = (SqlError)errorConstructor.Invoke(errorArguments);

        SqlErrorCollection errors = (SqlErrorCollection)Activator.CreateInstance(typeof(SqlErrorCollection), nonPublic: true)!;
        typeof(SqlErrorCollection)
            .GetMethod("Add", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(errors, [error]);

        MethodInfo createException = typeof(SqlException).GetMethod(
            "CreateException",
            BindingFlags.NonPublic | BindingFlags.Static,
            [typeof(SqlErrorCollection), typeof(string)])!;

        return (SqlException)createException.Invoke(null, [errors, "17.0.0"])!;
    }

    private static object? DefaultArgument(Type type)
    {
        if (type == typeof(string))
            return "Simulated SQL error";

        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }
}

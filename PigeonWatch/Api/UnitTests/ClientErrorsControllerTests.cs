using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.Core;
using PigeonWatch.WebApi.Controllers;
using PigeonWatch.WebApi.Models;

namespace PigeonWatch.UnitTests;

public class ClientErrorsControllerTests
{
    private readonly ILogger<ClientErrorsController> logger = Substitute.For<ILogger<ClientErrorsController>>();
    private readonly CancellationToken cancellationToken = TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("uncaught", LogLevel.Warning)]
    [InlineData("bootstrap", LogLevel.Warning)]
    [InlineData("navigation", LogLevel.Warning)]
    [InlineData("api-failure", LogLevel.Warning)]
    [InlineData("session-expired", LogLevel.Warning)]
    [InlineData("warmup-exhausted", LogLevel.Warning)]
    [InlineData("fallback", LogLevel.Information)]
    public async Task Report_logs_one_record_at_the_level_mapped_from_the_kind(string kind, LogLevel expectedLevel)
    {
        ClientErrorsController controller = new(logger);

        ActionResult<ClientErrorReceiptModel> result = await controller.Report(Report(kind), cancellationToken);

        LogCall call = Assert.Single(LogCalls());
        Assert.IsType<AcceptedResult>(result.Result);
        Assert.Equal(expectedLevel, call.Level);
    }

    [Fact]
    public async Task Report_never_logs_at_error_or_above_for_any_kind()
    {
        ClientErrorsController controller = new(logger);
        string[] kinds = ["uncaught", "bootstrap", "navigation", "api-failure", "session-expired", "warmup-exhausted", "fallback"];

        foreach (string kind in kinds)
            await controller.Report(Report(kind), cancellationToken);

        Assert.Equal(kinds.Length, LogCalls().Count);
        Assert.All(LogCalls(), call => Assert.True(call.Level < LogLevel.Error));
    }

    [Fact]
    public async Task Report_logs_the_allowlisted_fields_as_structured_values_and_the_client_trace_id()
    {
        Guid sessionId = Guid.NewGuid();
        ClientErrorsController controller = new(logger);
        ClientErrorReportModel report = new()
        {
            Kind = "api-failure",
            Context = "nearby.load",
            HttpStatus = 500,
            ErrorName = "HttpErrorResponse",
            TraceId = "00-abc123-def456-00",
            Route = "/sightings/:id",
            SessionId = sessionId
        };

        await controller.Report(report, cancellationToken);

        LogCall call = Assert.Single(LogCalls());
        Assert.Equal("api-failure", call.State["Kind"]);
        Assert.Equal("nearby.load", call.State["Context"]);
        Assert.Equal(500, call.State["HttpStatus"]);
        Assert.Equal("HttpErrorResponse", call.State["ErrorName"]);
        Assert.Equal("/sightings/:id", call.State["Route"]);
        Assert.Equal(sessionId, call.State["SessionId"]);
        Assert.Equal("00-abc123-def456-00", call.State["ClientTraceId"]);
        Assert.False(call.State.ContainsKey("TraceId"));
        Assert.DoesNotContain("nearby.load", call.Message);
    }

    [Fact]
    public async Task Report_logs_missing_optional_fields_as_null()
    {
        ClientErrorsController controller = new(logger);

        await controller.Report(Report("fallback"), cancellationToken);

        LogCall call = Assert.Single(LogCalls());
        Assert.Null(call.State["HttpStatus"]);
        Assert.Null(call.State["ErrorName"]);
        Assert.Null(call.State["ClientTraceId"]);
    }

    private static ClientErrorReportModel Report(string kind)
    {
        return new()
        {
            Kind = kind,
            Context = "storage.unavailable",
            Route = "/",
            SessionId = Guid.NewGuid()
        };
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

        string message = state["{OriginalFormat}"]?.ToString() ?? string.Empty;

        return new LogCall((LogLevel)arguments[0]!, state, message);
    }

    private sealed record LogCall(LogLevel Level, Dictionary<string, object?> State, string Message);
}

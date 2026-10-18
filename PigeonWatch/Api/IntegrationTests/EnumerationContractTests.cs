using System.Net;
using System.Text.Json.Nodes;

namespace PigeonWatch.IntegrationTests;

public sealed class EnumerationContractTests : IDisposable
{
    private const string email = "Taken.User@Example.com";
    private const string newEmail = "brand.new@example.com";
    private const string unknownEmail = "nobody.here@example.com";
    private const string displayName = "TakenUser";
    private const string password = "Passw0rd!one";
    private const string otherPassword = "Passw0rd!two";
    private const string wrongPassword = "Passw0rd!wrong";

    private readonly PigeonWatchApiFactory factory = new();
    private readonly CancellationToken cancellationToken = TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(email, password, displayName)]
    [InlineData(email, otherPassword, displayName)]
    [InlineData(email, password, "FreshName")]
    [InlineData("taken.user@example.com", password, "FreshName")]
    [InlineData("TAKEN.USER@EXAMPLE.COM", password, "FreshName")]
    [InlineData("  Taken.User@Example.com  ", password, "FreshName")]
    public async Task Register_failure_variants_for_an_existing_email_match_the_taken_display_name_answer(
        string attemptEmail,
        string attemptPassword,
        string attemptDisplayName)
    {
        await SeedAccountAsync();

        using HttpResponseMessage baseline = await RegisterFromFreshAddressAsync(newEmail, password, displayName);
        using HttpResponseMessage attempt = await RegisterFromFreshAddressAsync(attemptEmail, attemptPassword, attemptDisplayName);
        string baselineBody = await ReadBodyWithoutTraceIdAsync(baseline);
        string attemptBody = await ReadBodyWithoutTraceIdAsync(attempt);

        Assert.Equal(HttpStatusCode.BadRequest, baseline.StatusCode);
        Assert.Equal(["RegistrationFailed"], ReadErrorCodes(baselineBody));
        Assert.Equal(HttpStatusCode.BadRequest, attempt.StatusCode);
        Assert.Equal(baselineBody, attemptBody);
    }

    [Theory]
    [InlineData(email, password, displayName)]
    [InlineData(email, otherPassword, displayName)]
    [InlineData(email, password, "FreshName")]
    [InlineData("taken.user@example.com", password, "FreshName")]
    [InlineData("TAKEN.USER@EXAMPLE.COM", password, "FreshName")]
    [InlineData("  Taken.User@Example.com  ", password, "FreshName")]
    [InlineData(newEmail, password, displayName)]
    public async Task Register_failures_keep_the_account_count_and_echo_neither_email_nor_display_name(
        string attemptEmail,
        string attemptPassword,
        string attemptDisplayName)
    {
        await SeedAccountAsync();

        using HttpResponseMessage attempt = await RegisterFromFreshAddressAsync(attemptEmail, attemptPassword, attemptDisplayName);
        string body = await attempt.Content.ReadAsStringAsync(cancellationToken);
        string normalizedBody = await ReadBodyWithoutTraceIdAsync(attempt);

        Assert.Equal(HttpStatusCode.BadRequest, attempt.StatusCode);
        Assert.Equal(["RegistrationFailed"], ReadErrorCodes(normalizedBody));
        Assert.DoesNotContain(attemptEmail.Trim(), body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(attemptDisplayName, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(displayName, body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, factory.CountUserAccounts());
    }

    [Fact]
    public async Task Login_failure_answers_are_identical_for_an_unknown_email_and_a_wrong_password()
    {
        await SeedAccountAsync();

        using HttpResponseMessage wrongPasswordLogin = await LoginFromFreshAddressAsync(email, wrongPassword);
        using HttpResponseMessage unknownEmailLogin = await LoginFromFreshAddressAsync(unknownEmail, wrongPassword);
        string wrongPasswordBody = await ReadBodyWithoutTraceIdAsync(wrongPasswordLogin);
        string unknownEmailBody = await ReadBodyWithoutTraceIdAsync(unknownEmailLogin);

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPasswordLogin.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmailLogin.StatusCode);
        Assert.Equal(wrongPasswordBody, unknownEmailBody);
        Assert.Equal("Failed", ReadDetail(unknownEmailBody));
    }

    [Fact]
    public async Task Login_failure_for_an_unknown_email_never_answers_locked_out()
    {
        await SeedAccountAsync();

        List<string> details = [];

        for (int attemptNumber = 0; attemptNumber < 6; attemptNumber++)
        {
            using HttpResponseMessage login = await LoginFromFreshAddressAsync(unknownEmail, wrongPassword);
            string body = await ReadBodyWithoutTraceIdAsync(login);

            Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
            details.Add(ReadDetail(body));
        }

        Assert.All(details, detail => Assert.Equal("Failed", detail));
    }

    public void Dispose()
    {
        factory.Dispose();
    }

    private async Task SeedAccountAsync()
    {
        using HttpResponseMessage register = await RegisterFromFreshAddressAsync(email, password, displayName);

        register.EnsureSuccessStatusCode();
    }

    private async Task<HttpResponseMessage> RegisterFromFreshAddressAsync(string attemptEmail, string attemptPassword, string attemptDisplayName)
    {
        using HttpClient client = factory.CreateApiClient();
        HttpResponseMessage response = await new AccountApiClient(client).RegisterAsync(attemptEmail, attemptPassword, attemptDisplayName, cancellationToken);

        return response;
    }

    private async Task<HttpResponseMessage> LoginFromFreshAddressAsync(string attemptEmail, string attemptPassword)
    {
        using HttpClient client = factory.CreateApiClient();
        HttpResponseMessage response = await new AccountApiClient(client).LoginAsync(attemptEmail, attemptPassword, cancellationToken);

        return response;
    }

    private async Task<string> ReadBodyWithoutTraceIdAsync(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        JsonObject problem = JsonNode.Parse(body)!.AsObject();
        problem.Remove("traceId");

        return problem.ToJsonString();
    }

    private string[] ReadErrorCodes(string normalizedBody)
    {
        JsonObject problem = JsonNode.Parse(normalizedBody)!.AsObject();
        string[] codes = problem["errors"]!.AsObject().Select(property => property.Key).ToArray();

        return codes;
    }

    private string ReadDetail(string normalizedBody)
    {
        JsonObject problem = JsonNode.Parse(normalizedBody)!.AsObject();
        string detail = problem["detail"]!.GetValue<string>();

        return detail;
    }
}

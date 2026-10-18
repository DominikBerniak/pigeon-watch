using System.Net;

namespace PigeonWatch.IntegrationTests;

public sealed class RegisterContractTests : IDisposable
{
    private const string email = "first.user@example.com";
    private const string otherEmail = "second.user@example.com";
    private const string displayName = "FirstUser";
    private const string password = "Passw0rd!one";
    private const string otherPassword = "Passw0rd!two";

    private readonly PigeonWatchApiFactory factory = new();
    private readonly HttpClient httpClient;
    private readonly AccountApiClient accounts;
    private readonly CancellationToken cancellationToken = TestContext.Current.CancellationToken;

    public RegisterContractTests()
    {
        httpClient = factory.CreateApiClient();
        accounts = new AccountApiClient(httpClient);
    }

    [Fact]
    public async Task Register_creates_an_account_that_can_log_in()
    {
        HttpResponseMessage register = await accounts.RegisterAsync(email, password, displayName, cancellationToken);
        HttpResponseMessage login = await accounts.LoginAsync(email, password, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, register.StatusCode);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal(1, factory.CountUserAccounts());
    }

    [Fact]
    public async Task Register_repeat_returns_only_registration_failed_and_keeps_one_account()
    {
        await accounts.RegisterAsync(email, password, displayName, cancellationToken);

        HttpResponseMessage repeat = await accounts.RegisterAsync(email, password, displayName, cancellationToken);
        string body = await repeat.Content.ReadAsStringAsync(cancellationToken);
        string[] errorCodes = await accounts.ReadErrorCodesAsync(repeat, cancellationToken);
        HttpResponseMessage login = await accounts.LoginAsync(email, password, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, repeat.StatusCode);
        Assert.Equal(["RegistrationFailed"], errorCodes);
        Assert.DoesNotContain(email, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(displayName, body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, factory.CountUserAccounts());
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task Register_repeat_with_a_different_password_keeps_the_original_password_valid()
    {
        await accounts.RegisterAsync(email, password, displayName, cancellationToken);

        HttpResponseMessage repeat = await accounts.RegisterAsync(email, otherPassword, displayName, cancellationToken);
        string[] errorCodes = await accounts.ReadErrorCodesAsync(repeat, cancellationToken);
        HttpResponseMessage originalLogin = await accounts.LoginAsync(email, password, cancellationToken);
        HttpResponseMessage otherLogin = await accounts.LoginAsync(email, otherPassword, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, repeat.StatusCode);
        Assert.Equal(["RegistrationFailed"], errorCodes);
        Assert.Equal(HttpStatusCode.OK, originalLogin.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, otherLogin.StatusCode);
        Assert.Equal(1, factory.CountUserAccounts());
    }

    [Fact]
    public async Task Register_with_a_taken_display_name_answers_the_same_for_a_new_and_an_existing_email()
    {
        await accounts.RegisterAsync(email, password, displayName, cancellationToken);

        HttpResponseMessage newEmail = await accounts.RegisterAsync(otherEmail, password, displayName, cancellationToken);
        HttpResponseMessage existingEmail = await accounts.RegisterAsync(email, password, displayName, cancellationToken);
        string[] newEmailCodes = await accounts.ReadErrorCodesAsync(newEmail, cancellationToken);
        string[] existingEmailCodes = await accounts.ReadErrorCodesAsync(existingEmail, cancellationToken);
        string newEmailBody = await AccountApiClient.ReadBodyWithoutTraceIdAsync(newEmail, cancellationToken);
        string existingEmailBody = await AccountApiClient.ReadBodyWithoutTraceIdAsync(existingEmail, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, newEmail.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, existingEmail.StatusCode);
        Assert.Equal(["RegistrationFailed"], newEmailCodes);
        Assert.Equal(["RegistrationFailed"], existingEmailCodes);
        Assert.Equal(existingEmailBody, newEmailBody);
        Assert.DoesNotContain(displayName, newEmailBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(otherEmail, newEmailBody, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, factory.CountUserAccounts());
    }

    [Fact]
    public async Task Register_with_a_taken_display_name_differing_by_case_or_padding_also_answers_registration_failed_only()
    {
        await accounts.RegisterAsync(email, password, displayName, cancellationToken);

        HttpResponseMessage otherCase = await accounts.RegisterAsync(otherEmail, password, displayName.ToUpperInvariant(), cancellationToken);
        HttpResponseMessage padded = await accounts.RegisterAsync(otherEmail, password, $"  {displayName}  ", cancellationToken);
        string[] otherCaseCodes = await accounts.ReadErrorCodesAsync(otherCase, cancellationToken);
        string[] paddedCodes = await accounts.ReadErrorCodesAsync(padded, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, otherCase.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, padded.StatusCode);
        Assert.Equal(["RegistrationFailed"], otherCaseCodes);
        Assert.Equal(["RegistrationFailed"], paddedCodes);
        Assert.Equal(1, factory.CountUserAccounts());
    }

    public void Dispose()
    {
        httpClient.Dispose();
        factory.Dispose();
    }
}

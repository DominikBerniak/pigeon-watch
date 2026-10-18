using System.Text;
using System.Text.Json;

namespace PigeonWatch.IntegrationTests;

public sealed class EndpointFixtureRegistry
{
    public static readonly string[] NotFoundStubKeys =
    [
        "POST auth/register",
        "GET auth/confirmEmail",
        "POST auth/resendConfirmationEmail",
        "POST auth/forgotPassword",
        "POST auth/resetPassword"
    ];

    public static readonly string[] ManageStubKeys =
    [
        "POST auth/manage/2fa",
        "GET auth/manage/info",
        "POST auth/manage/info"
    ];

    private const string validPassword = "Passw0rd!sweep";
    private const string wrongPassword = "Wr0ngPassw0rd!";
    private const string unknownEmail = "nobody.registered@example.org";

    private static readonly string[] malformedBodies = ["", "not json", "{}"];

    public IReadOnlyList<EndpointFixture> Fixtures { get; } =
    [
        new(["POST account/register"], BuildRegister),
        new(["PUT account/profile"], BuildProfile),
        new(["PUT account/password"], BuildPassword),
        new(["GET configuration/client"], BuildConfigurationClient),
        new(["GET configuration/general"], BuildConfigurationGeneral),
        new(["GET resources/{culture}"], BuildResources),
        new(["POST sightings"], BuildCreateSighting),
        new(["GET sightings/{id:guid}"], BuildGetSighting),
        new(["GET sightings/nearby"], BuildNearby),
        new(["GET sightings/mine"], BuildMine),
        new(["POST auth/login"], BuildLogin),
        new(["POST auth/refresh"], BuildRefresh),
        new([.. NotFoundStubKeys, .. ManageStubKeys], BuildStubs)
    ];

    public IReadOnlyList<EndpointExemption> Exemptions { get; } = [];

    public HashSet<string> CoveredKeys()
    {
        HashSet<string> keys = [];

        foreach (EndpointFixture fixture in Fixtures)
            keys.UnionWith(fixture.Keys);

        foreach (EndpointExemption exemption in Exemptions)
            keys.Add(exemption.Key);

        return keys;
    }

    public IReadOnlyList<FixtureRequest> BuildRequests(string key, FixtureContext context)
    {
        EndpointFixture? fixture = Fixtures.FirstOrDefault(candidate => candidate.Keys.Contains(key));

        if (fixture is null)
            throw new InvalidOperationException($"No fixture covers {key}.");

        List<FixtureRequest> requests = fixture.Build(context).Where(request => request.Key == key).ToList();

        return requests;
    }

    private static IReadOnlyList<FixtureRequest> BuildRegister(FixtureContext context)
    {
        const string key = "POST account/register";
        const string uri = "/account/register";
        string suffix = NewSuffix();
        List<FixtureRequest> requests =
        [
            Make(key, "fresh account", JsonOf(HttpMethod.Post, uri, new { email = $"sweep.{suffix}@example.org", password = validPassword, displayName = $"Sweep{suffix}" })),
            Make(key, "invalid email", JsonOf(HttpMethod.Post, uri, new { email = "not-an-email", password = validPassword, displayName = $"Sweep{suffix}Bad" })),
            Make(key, "taken display name with a fresh email", JsonOf(HttpMethod.Post, uri, new { email = $"sweep.taken.{suffix}@example.org", password = validPassword, displayName = context.RegisteredDisplayNames[0] }))
        ];

        for (int index = 0; index < context.RegisteredEmails.Count; index++)
        {
            string email = context.RegisteredEmails[index];

            requests.Add(Make(key, $"registered email {index}", JsonOf(HttpMethod.Post, uri, new { email, password = validPassword, displayName = $"Sweep{suffix}{index}" })));
            requests.Add(Make(key, $"registered email {index} upper-cased and padded", JsonOf(HttpMethod.Post, uri, new { email = $" {email.ToUpperInvariant()} ", password = validPassword, displayName = $"Sweep{suffix}{index}x" })));
        }

        AddMalformed(requests, key, HttpMethod.Post, uri);

        return requests;
    }

    private static IReadOnlyList<FixtureRequest> BuildProfile(FixtureContext context)
    {
        const string key = "PUT account/profile";
        const string uri = "/account/profile";
        string suffix = NewSuffix();
        List<FixtureRequest> requests =
        [
            Make(key, "unique new name", JsonOf(HttpMethod.Put, uri, new { displayName = $"Renamed{suffix}" })),
            Make(key, "taken display name", JsonOf(HttpMethod.Put, uri, new { displayName = context.RegisteredDisplayNames[0] }))
        ];

        for (int index = 0; index < context.RegisteredEmails.Count; index++)
            requests.Add(Make(key, $"display name holding registered email {index}", JsonOf(HttpMethod.Put, uri, new { displayName = context.RegisteredEmails[index] })));

        AddMalformed(requests, key, HttpMethod.Put, uri);

        return requests;
    }

    private static IReadOnlyList<FixtureRequest> BuildPassword(FixtureContext context)
    {
        const string key = "PUT account/password";
        const string uri = "/account/password";
        List<FixtureRequest> requests =
        [
            Make(key, "wrong current password", JsonOf(HttpMethod.Put, uri, new { currentPassword = wrongPassword, newPassword = validPassword })),
            Make(key, "weak new password", JsonOf(HttpMethod.Put, uri, new { currentPassword = wrongPassword, newPassword = "short" }))
        ];

        AddMalformed(requests, key, HttpMethod.Put, uri);

        return requests;
    }

    private static IReadOnlyList<FixtureRequest> BuildConfigurationClient(FixtureContext context)
    {
        return [Make("GET configuration/client", "client configuration", new HttpRequestMessage(HttpMethod.Get, "/configuration/client"))];
    }

    private static IReadOnlyList<FixtureRequest> BuildConfigurationGeneral(FixtureContext context)
    {
        return [Make("GET configuration/general", "general configuration", new HttpRequestMessage(HttpMethod.Get, "/configuration/general"))];
    }

    private static IReadOnlyList<FixtureRequest> BuildResources(FixtureContext context)
    {
        const string key = "GET resources/{culture}";

        return
        [
            Make(key, "english", new HttpRequestMessage(HttpMethod.Get, "/resources/en")),
            Make(key, "unknown culture", new HttpRequestMessage(HttpMethod.Get, "/resources/xx"))
        ];
    }

    private static IReadOnlyList<FixtureRequest> BuildCreateSighting(FixtureContext context)
    {
        const string key = "POST sightings";
        const string uri = "/sightings";
        List<FixtureRequest> requests =
        [
            Make(key, "valid sighting", JsonOf(HttpMethod.Post, uri, SightingBody(1, 2, 52.2297, 21.0122))),
            Make(key, "invalid initial status", JsonOf(HttpMethod.Post, uri, SightingBody(99, 2, 52.2297, 21.0122))),
            Make(key, "invalid criticality", JsonOf(HttpMethod.Post, uri, SightingBody(1, 99, 52.2297, 21.0122))),
            Make(key, "out of range location", JsonOf(HttpMethod.Post, uri, SightingBody(1, 2, 200, 21.0122)))
        ];

        AddMalformed(requests, key, HttpMethod.Post, uri);

        return requests;
    }

    private static IReadOnlyList<FixtureRequest> BuildGetSighting(FixtureContext context)
    {
        const string key = "GET sightings/{id:guid}";

        return
        [
            Make(key, "existing sighting", new HttpRequestMessage(HttpMethod.Get, $"/sightings/{context.SightingId}")),
            Make(key, "unknown sighting", new HttpRequestMessage(HttpMethod.Get, $"/sightings/{Guid.NewGuid()}"))
        ];
    }

    private static IReadOnlyList<FixtureRequest> BuildNearby(FixtureContext context)
    {
        const string key = "GET sightings/nearby";

        return
        [
            Make(key, "in range", new HttpRequestMessage(HttpMethod.Get, "/sightings/nearby?latitude=52.2297&longitude=21.0122")),
            Make(key, "latitude out of range", new HttpRequestMessage(HttpMethod.Get, "/sightings/nearby?latitude=999&longitude=21.0122")),
            Make(key, "missing coordinates", new HttpRequestMessage(HttpMethod.Get, "/sightings/nearby")),
            Make(key, "non numeric coordinates", new HttpRequestMessage(HttpMethod.Get, "/sightings/nearby?latitude=abc&longitude=def"))
        ];
    }

    private static IReadOnlyList<FixtureRequest> BuildMine(FixtureContext context)
    {
        return [Make("GET sightings/mine", "own sightings", new HttpRequestMessage(HttpMethod.Get, "/sightings/mine"))];
    }

    private static IReadOnlyList<FixtureRequest> BuildLogin(FixtureContext context)
    {
        const string key = "POST auth/login";
        const string uri = "/auth/login";
        List<FixtureRequest> requests = [];

        for (int index = 0; index < context.RegisteredEmails.Count; index++)
            requests.Add(Make(key, $"wrong password for registered email {index}", JsonOf(HttpMethod.Post, uri, new { email = context.RegisteredEmails[index], password = wrongPassword })));

        requests.Add(Make(key, "unknown email", JsonOf(HttpMethod.Post, uri, new { email = unknownEmail, password = wrongPassword })));
        requests.Add(Make(key, "cookie sign-in requested", JsonOf(HttpMethod.Post, "/auth/login?useCookies=true", new { email = context.RegisteredEmails[0], password = wrongPassword })));

        return requests;
    }

    private static IReadOnlyList<FixtureRequest> BuildRefresh(FixtureContext context)
    {
        return [Make("POST auth/refresh", "garbage refresh token", JsonOf(HttpMethod.Post, "/auth/refresh", new { refreshToken = "garbage" }))];
    }

    private static IReadOnlyList<FixtureRequest> BuildStubs(FixtureContext context)
    {
        object body = new { email = context.RegisteredEmails[0], password = validPassword };
        List<FixtureRequest> requests =
        [
            Make("POST auth/register", "stubbed register", JsonOf(HttpMethod.Post, "/auth/register", body)),
            Make("GET auth/confirmEmail", "stubbed confirm email", new HttpRequestMessage(HttpMethod.Get, $"/auth/confirmEmail?userId={Guid.NewGuid()}&code=garbage")),
            Make("POST auth/resendConfirmationEmail", "stubbed resend confirmation", JsonOf(HttpMethod.Post, "/auth/resendConfirmationEmail", body)),
            Make("POST auth/forgotPassword", "stubbed forgot password", JsonOf(HttpMethod.Post, "/auth/forgotPassword", body)),
            Make("POST auth/resetPassword", "stubbed reset password", JsonOf(HttpMethod.Post, "/auth/resetPassword", body)),
            Make("POST auth/manage/2fa", "stubbed two factor", JsonOf(HttpMethod.Post, "/auth/manage/2fa", body)),
            Make("GET auth/manage/info", "stubbed read info", new HttpRequestMessage(HttpMethod.Get, "/auth/manage/info")),
            Make("POST auth/manage/info", "stubbed update info", JsonOf(HttpMethod.Post, "/auth/manage/info", body))
        ];

        return requests;
    }

    private static object SightingBody(int status, int criticality, double latitude, double longitude)
    {
        return new
        {
            name = "Sweep pigeon",
            description = "A pigeon with a bent wing near the fountain.",
            behaviour = "Hops instead of flying.",
            latitude,
            longitude,
            status,
            criticality
        };
    }

    private static void AddMalformed(List<FixtureRequest> requests, string key, HttpMethod method, string uri)
    {
        for (int index = 0; index < malformedBodies.Length; index++)
            requests.Add(Make(key, $"malformed body {index}", Json(method, uri, malformedBodies[index])));
    }

    private static FixtureRequest Make(string key, string description, HttpRequestMessage message)
    {
        return new FixtureRequest(key, description, message);
    }

    private static HttpRequestMessage Json(HttpMethod method, string uri, string body)
    {
        HttpRequestMessage message = new(method, uri)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        return message;
    }

    private static HttpRequestMessage JsonOf(HttpMethod method, string uri, object body)
    {
        return Json(method, uri, JsonSerializer.Serialize(body));
    }

    private static string NewSuffix()
    {
        return Guid.NewGuid().ToString("N")[..8];
    }
}

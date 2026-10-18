namespace PigeonWatch.IntegrationTests;

public sealed record EndpointFixture(
    IReadOnlyList<string> Keys,
    Func<FixtureContext, IReadOnlyList<FixtureRequest>> Build);

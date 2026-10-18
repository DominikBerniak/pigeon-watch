namespace PigeonWatch.IntegrationTests;

public sealed class EndpointCoverageTests : IDisposable
{
    private readonly PigeonWatchApiFactory factory = new();
    private readonly EndpointFixtureRegistry registry = new();

    [Fact]
    public void Every_routed_endpoint_has_a_fixture_or_an_exemption()
    {
        IReadOnlyList<RoutedEndpoint> endpoints = new EndpointCatalog(factory.Services).GetEndpoints();
        HashSet<string> covered = registry.CoveredKeys();
        List<string> uncovered = endpoints
            .Where(endpoint => !covered.Contains(endpoint.Key))
            .Select(endpoint => endpoint.Key)
            .ToList();

        Assert.True(endpoints.Count > 0, "The endpoint catalog is empty, so the guard would be vacuous.");
        Assert.True(
            uncovered.Count == 0,
            $"Routed endpoints without a fixture or an exemption:{Environment.NewLine}{string.Join(Environment.NewLine, uncovered)}");
    }

    [Fact]
    public void Every_fixture_and_exemption_matches_a_routed_endpoint()
    {
        HashSet<string> routed = new EndpointCatalog(factory.Services).GetEndpoints()
            .Select(endpoint => endpoint.Key)
            .ToHashSet();
        List<string> declared = registry.Fixtures
            .SelectMany(fixture => fixture.Keys)
            .Concat(registry.Exemptions.Select(exemption => exemption.Key))
            .ToList();
        List<string> stale = declared.Where(key => !routed.Contains(key)).ToList();
        List<string> duplicated = declared
            .GroupBy(key => key)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        Assert.True(declared.Count > 0, "No fixture or exemption is declared, so the guard would be vacuous.");
        Assert.True(
            stale.Count == 0,
            $"Fixtures or exemptions that match no routed endpoint:{Environment.NewLine}{string.Join(Environment.NewLine, stale)}");
        Assert.True(
            duplicated.Count == 0,
            $"Keys declared more than once:{Environment.NewLine}{string.Join(Environment.NewLine, duplicated)}");
        Assert.All(registry.Exemptions, exemption => Assert.False(string.IsNullOrWhiteSpace(exemption.Reason)));
    }

    public void Dispose()
    {
        factory.Dispose();
    }
}

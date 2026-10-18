namespace PigeonWatch.IntegrationTests;

public sealed class PrivacySweep(
    PigeonWatchApiFactory factory,
    EndpointFixtureRegistry registry,
    ResponseLeakScanner scanner)
{
    public async Task<IReadOnlyList<string>> RunAsync(
        string caller,
        string? accessToken,
        IReadOnlyList<string> foreignEmails,
        FixtureContext context,
        CancellationToken cancellationToken = default)
    {
        List<string> needles = foreignEmails.SelectMany(email => ResponseLeakScanner.NeedlesFor(email)).ToList();
        List<string> failures = [];
        HashSet<string> requestedKeys = [];

        foreach (EndpointFixture fixture in registry.Fixtures)
        {
            foreach (FixtureRequest request in fixture.Build(context))
            {
                requestedKeys.Add(request.Key);

                using HttpResponseMessage response = await factory.SendFromFreshAddressAsync(request.Message, accessToken, cancellationToken);
                int status = (int)response.StatusCode;
                string label = $"{caller}: {request.Key} ({request.Description})";

                if (status == 429)
                    failures.Add($"{label} answered 429, so address isolation broke");

                if (status >= 500)
                    failures.Add($"{label} answered {status}, so the check would be vacuous");

                IReadOnlyList<LeakHit> hits = await scanner.ScanAsync(response, needles, cancellationToken);

                foreach (LeakHit hit in hits)
                    failures.Add($"{label} leaked a foreign email in {hit.Location}");

                request.Message.Dispose();
            }
        }

        IReadOnlyList<RoutedEndpoint> endpoints = new EndpointCatalog(factory.Services).GetEndpoints();
        HashSet<string> exempt = registry.Exemptions.Select(exemption => exemption.Key).ToHashSet();

        foreach (RoutedEndpoint endpoint in endpoints)
        {
            if (!requestedKeys.Contains(endpoint.Key) && !exempt.Contains(endpoint.Key))
                failures.Add($"{caller}: no request was sent to {endpoint.Key}");
        }

        return failures;
    }
}

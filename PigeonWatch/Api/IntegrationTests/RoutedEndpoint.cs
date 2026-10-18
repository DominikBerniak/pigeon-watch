namespace PigeonWatch.IntegrationTests;

public sealed record RoutedEndpoint(string Method, string Pattern, bool RequiresAuthorization)
{
    public string Key => $"{Method} {Pattern}";
}

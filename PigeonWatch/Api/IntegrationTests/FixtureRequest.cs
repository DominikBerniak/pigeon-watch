namespace PigeonWatch.IntegrationTests;

public sealed record FixtureRequest(string Key, string Description, HttpRequestMessage Message);

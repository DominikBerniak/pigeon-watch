using System.Net;
using System.Text.Json;

namespace PigeonWatch.IntegrationTests;

public sealed class ResponseLeakScanner
{
    public static IReadOnlyList<string> NeedlesFor(string email)
    {
        List<string> needles = new[]
            {
                email,
                email.ToUpperInvariant(),
                Uri.EscapeDataString(email),
                WebUtility.UrlEncode(email)
            }
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return needles;
    }

    public async Task<IReadOnlyList<LeakHit>> ScanAsync(
        HttpResponseMessage response,
        IEnumerable<string> needles,
        CancellationToken cancellationToken = default)
    {
        List<string> needleList = needles.ToList();
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        List<HaystackSegment> segments = [new HaystackSegment("status line", $"{(int)response.StatusCode} {response.ReasonPhrase}")];

        foreach (KeyValuePair<string, IEnumerable<string>> header in response.Headers.Concat(response.Content.Headers))
        {
            foreach (string value in header.Value)
                segments.Add(new HaystackSegment($"header {header.Key}", value));
        }

        segments.Add(new HaystackSegment("body", body));
        CollectJson(body, segments);

        List<LeakHit> hits = [];

        foreach (HaystackSegment segment in segments)
        {
            foreach (string needle in needleList)
            {
                if (segment.Text.Contains(needle, StringComparison.OrdinalIgnoreCase))
                    hits.Add(new LeakHit(segment.Location, needle));
            }
        }

        return hits;
    }

    private static void CollectJson(string body, List<HaystackSegment> segments)
    {
        if (string.IsNullOrWhiteSpace(body))
            return;

        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            Walk(document.RootElement, "$", segments);
        }
        catch (JsonException)
        {
            return;
        }
    }

    private static void Walk(JsonElement element, string path, List<HaystackSegment> segments)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                segments.Add(new HaystackSegment($"json property name under {path}", property.Name));
                Walk(property.Value, $"{path}.{property.Name}", segments);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            int index = 0;

            foreach (JsonElement item in element.EnumerateArray())
            {
                Walk(item, $"{path}[{index}]", segments);
                index++;
            }
        }
        else if (element.ValueKind == JsonValueKind.String)
            segments.Add(new HaystackSegment($"json string value at {path}", element.GetString() ?? string.Empty));
    }

    private sealed record HaystackSegment(string Location, string Text);
}

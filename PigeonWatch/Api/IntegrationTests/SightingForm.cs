using System.Globalization;
using System.Net.Http.Headers;

namespace PigeonWatch.IntegrationTests;

public sealed class SightingForm
{
    public const string PhotoFieldName = "photo";
    public const string PhotoFileName = "photo.jpg";
    public const string JpegContentType = "image/jpeg";

    public static Dictionary<string, string> Fields(
        int status = 1,
        int criticality = 2,
        double latitude = 52.2297,
        double longitude = 21.0122,
        string name = "Test pigeon",
        string description = "A pigeon with a bent wing near the fountain.",
        string behaviour = "Hops instead of flying.")
    {
        Dictionary<string, string> fields = new(StringComparer.Ordinal)
        {
            ["name"] = name,
            ["description"] = description,
            ["behaviour"] = behaviour,
            ["latitude"] = latitude.ToString(CultureInfo.InvariantCulture),
            ["longitude"] = longitude.ToString(CultureInfo.InvariantCulture),
            ["status"] = status.ToString(CultureInfo.InvariantCulture),
            ["criticality"] = criticality.ToString(CultureInfo.InvariantCulture)
        };

        return fields;
    }

    public static MultipartFormDataContent Content(
        IReadOnlyDictionary<string, string> fields,
        byte[]? photo = null,
        string photoContentType = JpegContentType)
    {
        MultipartFormDataContent content = new();

        foreach (KeyValuePair<string, string> field in fields)
            content.Add(new StringContent(field.Value), field.Key);

        if (photo is not null)
        {
            ByteArrayContent photoContent = new(photo);
            photoContent.Headers.ContentType = new MediaTypeHeaderValue(photoContentType);
            content.Add(photoContent, PhotoFieldName, PhotoFileName);
        }

        return content;
    }

    public static HttpRequestMessage Request(
        IReadOnlyDictionary<string, string> fields,
        byte[]? photo = null,
        string photoContentType = JpegContentType)
    {
        HttpRequestMessage message = new(HttpMethod.Post, "/sightings")
        {
            Content = Content(fields, photo, photoContentType)
        };

        return message;
    }
}

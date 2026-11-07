using Microsoft.AspNetCore.Http.Features;

namespace PigeonWatch.IntegrationTests;

public sealed class TestMaxRequestBodySizeFeature : IHttpMaxRequestBodySizeFeature
{
    public bool IsReadOnly
    {
        get
        {
            return false;
        }
    }

    public long? MaxRequestBodySize { get; set; }
}

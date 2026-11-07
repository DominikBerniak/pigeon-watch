using Azure;
using PigeonWatch.Data.Storage;

namespace PigeonWatch.UnitTests;

public class AzureBlobSightingPhotoStoreTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(408)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    public void A_transient_request_failure_is_storage_unavailable(int status)
    {
        RequestFailedException exception = new(status, "Request failed.");

        bool unavailable = AzureBlobSightingPhotoStore.IsUnavailable(exception, CancellationToken.None);

        Assert.True(unavailable);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(409)]
    public void A_configuration_request_failure_is_not_storage_unavailable(int status)
    {
        RequestFailedException exception = new(status, "Request failed.");

        bool unavailable = AzureBlobSightingPhotoStore.IsUnavailable(exception, CancellationToken.None);

        Assert.False(unavailable);
    }
}

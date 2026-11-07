using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using PigeonWatch.BusinessObjects;

namespace PigeonWatch.Data.Storage;

internal sealed class AzureBlobSightingPhotoStore(BlobStorageOptions options) : ISightingPhotoStore
{
    private const string jpegContentType = "image/jpeg";

    private readonly BlobContainerClient container =
        new BlobServiceClient(
            options.ServiceUri ?? throw new InvalidOperationException($"{BlobStorageOptions.SectionName}:{nameof(BlobStorageOptions.ServiceUri)} is not configured."),
            new DefaultAzureCredential())
        .GetBlobContainerClient(options.ContainerName);

    public async Task SaveAsync(string blobName, Stream jpeg, CancellationToken cancellationToken = default)
    {
        BlobClient blob = container.GetBlobClient(blobName);
        BlobUploadOptions uploadOptions = new()
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = jpegContentType }
        };

        try
        {
            await blob.UploadAsync(jpeg, uploadOptions, cancellationToken);
        }
        catch (Exception exception) when (IsUnavailable(exception, cancellationToken))
        {
            throw new PhotoStorageUnavailableException($"Photo storage could not save blob {blobName}.", exception);
        }
    }

    public async Task<Stream?> OpenReadAsync(string blobName, CancellationToken cancellationToken = default)
    {
        BlobClient blob = container.GetBlobClient(blobName);

        try
        {
            Response<BlobDownloadStreamingResult> response = await blob.DownloadStreamingAsync(cancellationToken: cancellationToken);

            return response.Value.Content;
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return null;
        }
        catch (Exception exception) when (IsUnavailable(exception, cancellationToken))
        {
            throw new PhotoStorageUnavailableException($"Photo storage could not read blob {blobName}.", exception);
        }
    }

    public async Task DeleteAsync(string blobName, CancellationToken cancellationToken = default)
    {
        BlobClient blob = container.GetBlobClient(blobName);

        try
        {
            await blob.DeleteIfExistsAsync(cancellationToken: cancellationToken);
        }
        catch (Exception exception) when (IsUnavailable(exception, cancellationToken))
        {
            throw new PhotoStorageUnavailableException($"Photo storage could not delete blob {blobName}.", exception);
        }
    }

    internal static bool IsUnavailable(Exception exception, CancellationToken cancellationToken)
    {
        if (exception is RequestFailedException requestFailed)
            return requestFailed.Status is 0 or 408 or 429 or >= 500;

        if (exception is AuthenticationFailedException or AggregateException or TimeoutException)
            return true;

        return exception is OperationCanceledException && !cancellationToken.IsCancellationRequested;
    }
}

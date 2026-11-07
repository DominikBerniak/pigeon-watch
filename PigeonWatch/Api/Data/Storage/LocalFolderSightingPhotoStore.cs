using PigeonWatch.BusinessObjects;

namespace PigeonWatch.Data.Storage;

internal sealed class LocalFolderSightingPhotoStore(string localDirectory) : ISightingPhotoStore
{
    private const string jpegExtension = ".jpg";

    private readonly string rootDirectory = Path.GetFullPath(localDirectory);

    public async Task SaveAsync(string blobName, Stream jpeg, CancellationToken cancellationToken = default)
    {
        string path = GetPath(blobName);

        try
        {
            Directory.CreateDirectory(rootDirectory);
            await using FileStream file = new(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await jpeg.CopyToAsync(file, cancellationToken);
        }
        catch (Exception exception) when (IsUnavailable(exception))
        {
            throw new PhotoStorageUnavailableException($"Photo storage could not save blob {blobName}.", exception);
        }
    }

    public Task<Stream?> OpenReadAsync(string blobName, CancellationToken cancellationToken = default)
    {
        string path = GetPath(blobName);

        if (!File.Exists(path))
            return Task.FromResult<Stream?>(null);

        try
        {
            Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);

            return Task.FromResult<Stream?>(stream);
        }
        catch (FileNotFoundException)
        {
            return Task.FromResult<Stream?>(null);
        }
        catch (Exception exception) when (IsUnavailable(exception))
        {
            throw new PhotoStorageUnavailableException($"Photo storage could not read blob {blobName}.", exception);
        }
    }

    public Task DeleteAsync(string blobName, CancellationToken cancellationToken = default)
    {
        string path = GetPath(blobName);

        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception exception) when (IsUnavailable(exception))
        {
            throw new PhotoStorageUnavailableException($"Photo storage could not delete blob {blobName}.", exception);
        }

        return Task.CompletedTask;
    }

    private static bool IsUnavailable(Exception exception)
    {
        return exception is IOException or UnauthorizedAccessException;
    }

    private string GetPath(string blobName)
    {
        if (!IsValidBlobName(blobName))
            throw new ArgumentException($"Blob name must be a GUID followed by {jpegExtension} or {SightingRules.PhotoThumbnailBlobSuffix}{jpegExtension}.", nameof(blobName));

        return Path.Combine(rootDirectory, blobName);
    }

    private static bool IsValidBlobName(string blobName)
    {
        if (!blobName.EndsWith(jpegExtension, StringComparison.Ordinal))
            return false;

        string stem = blobName[..^jpegExtension.Length];

        if (stem.EndsWith(SightingRules.PhotoThumbnailBlobSuffix, StringComparison.Ordinal))
            stem = stem[..^SightingRules.PhotoThumbnailBlobSuffix.Length];

        return Guid.TryParseExact(stem, "D", out _);
    }
}

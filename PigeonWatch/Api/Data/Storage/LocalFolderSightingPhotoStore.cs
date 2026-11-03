namespace PigeonWatch.Data.Storage;

internal sealed class LocalFolderSightingPhotoStore(string localDirectory) : ISightingPhotoStore
{
    private const string jpegExtension = ".jpg";

    private readonly string rootDirectory = Path.GetFullPath(localDirectory);

    public async Task SaveAsync(string blobName, Stream jpeg, CancellationToken cancellationToken = default)
    {
        string path = GetPath(blobName);
        Directory.CreateDirectory(rootDirectory);
        await using FileStream file = new(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await jpeg.CopyToAsync(file, cancellationToken);
    }

    public Task<Stream?> OpenReadAsync(string blobName, CancellationToken cancellationToken = default)
    {
        string path = GetPath(blobName);

        if (!File.Exists(path))
            return Task.FromResult<Stream?>(null);

        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);

        return Task.FromResult<Stream?>(stream);
    }

    public Task DeleteAsync(string blobName, CancellationToken cancellationToken = default)
    {
        string path = GetPath(blobName);

        if (File.Exists(path))
            File.Delete(path);

        return Task.CompletedTask;
    }

    private string GetPath(string blobName)
    {
        if (!IsValidBlobName(blobName))
            throw new ArgumentException($"Blob name must be a GUID followed by {jpegExtension}.", nameof(blobName));

        return Path.Combine(rootDirectory, blobName);
    }

    private static bool IsValidBlobName(string blobName)
    {
        if (!blobName.EndsWith(jpegExtension, StringComparison.Ordinal))
            return false;

        string stem = blobName[..^jpegExtension.Length];

        return Guid.TryParseExact(stem, "D", out _);
    }
}

using System.Collections.Concurrent;
using PigeonWatch.BusinessObjects;
using PigeonWatch.Data.Storage;

namespace PigeonWatch.IntegrationTests;

public sealed class InMemorySightingPhotoStore : ISightingPhotoStore
{
    private readonly ConcurrentDictionary<string, byte[]> blobs = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> savedBlobNames = new();
    private volatile bool isUnavailable;
    private volatile Func<string, bool>? failSaveWhen;

    public bool IsUnavailable
    {
        get
        {
            return isUnavailable;
        }

        set
        {
            isUnavailable = value;
        }
    }

    public Func<string, bool>? FailSaveWhen
    {
        get
        {
            return failSaveWhen;
        }

        set
        {
            failSaveWhen = value;
        }
    }

    public IReadOnlyList<string> SavedBlobNames
    {
        get
        {
            return savedBlobNames.ToList();
        }
    }

    public IReadOnlyCollection<string> BlobNames
    {
        get
        {
            return blobs.Keys.ToList();
        }
    }

    public async Task SaveAsync(string blobName, Stream jpeg, CancellationToken cancellationToken = default)
    {
        ThrowIfUnavailable("save", blobName);

        if (failSaveWhen?.Invoke(blobName) == true)
            throw Unavailable("save", blobName);

        using MemoryStream copy = new();
        await jpeg.CopyToAsync(copy, cancellationToken);
        blobs[blobName] = copy.ToArray();
        savedBlobNames.Enqueue(blobName);
    }

    public Task<Stream?> OpenReadAsync(string blobName, CancellationToken cancellationToken = default)
    {
        ThrowIfUnavailable("read", blobName);

        if (!blobs.TryGetValue(blobName, out byte[]? bytes))
            return Task.FromResult<Stream?>(null);

        Stream stream = new MemoryStream(bytes, writable: false);

        return Task.FromResult<Stream?>(stream);
    }

    public Task DeleteAsync(string blobName, CancellationToken cancellationToken = default)
    {
        ThrowIfUnavailable("delete", blobName);

        blobs.TryRemove(blobName, out byte[]? _);

        return Task.CompletedTask;
    }

    public void Remove(string blobName)
    {
        blobs.TryRemove(blobName, out byte[]? _);
    }

    public byte[]? Read(string blobName)
    {
        return blobs.TryGetValue(blobName, out byte[]? bytes) ? bytes : null;
    }

    private void ThrowIfUnavailable(string operation, string blobName)
    {
        if (isUnavailable)
            throw Unavailable(operation, blobName);
    }

    private static PhotoStorageUnavailableException Unavailable(string operation, string blobName)
    {
        return new PhotoStorageUnavailableException(
            $"Photo storage could not {operation} blob {blobName}.",
            new IOException("The in-memory photo store is switched to fail."));
    }
}

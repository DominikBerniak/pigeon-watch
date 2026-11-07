namespace PigeonWatch.Data.Storage;

public interface ISightingPhotoStore
{
    Task SaveAsync(string blobName, Stream jpeg, CancellationToken cancellationToken = default);
    Task<Stream?> OpenReadAsync(string blobName, CancellationToken cancellationToken = default);
    Task DeleteAsync(string blobName, CancellationToken cancellationToken = default);
}

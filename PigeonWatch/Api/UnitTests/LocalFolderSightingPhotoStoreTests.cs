using PigeonWatch.Data.Storage;

namespace PigeonWatch.UnitTests;

public sealed class LocalFolderSightingPhotoStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "PigeonWatchPhotoStoreTests", Guid.NewGuid().ToString("N"));
    private readonly CancellationToken cancellationToken = TestContext.Current.CancellationToken;

    public static TheoryData<string> InvalidBlobNames => new()
    {
        "../escape.jpg",
        "..\\escape.jpg",
        "not-a-guid.jpg",
        $"{Guid.NewGuid()}.png",
        $"{Guid.NewGuid()}",
        $"../{Guid.NewGuid()}.jpg",
        $"sub/{Guid.NewGuid()}.jpg",
        string.Empty
    };

    [Fact]
    public async Task Save_open_and_delete_round_trip_the_bytes()
    {
        LocalFolderSightingPhotoStore store = new(directory);
        string blobName = $"{Guid.NewGuid()}.jpg";
        byte[] bytes = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 0xFF, 0xD9];

        using (MemoryStream source = new(bytes))
            await store.SaveAsync(blobName, source, cancellationToken);

        await using (Stream? opened = await store.OpenReadAsync(blobName, cancellationToken))
        {
            Assert.NotNull(opened);
            using MemoryStream copy = new();
            await opened.CopyToAsync(copy, cancellationToken);
            Assert.Equal(bytes, copy.ToArray());
        }

        await store.DeleteAsync(blobName, cancellationToken);

        Assert.Null(await store.OpenReadAsync(blobName, cancellationToken));
    }

    [Fact]
    public async Task Open_of_a_missing_blob_returns_null()
    {
        LocalFolderSightingPhotoStore store = new(directory);

        Stream? opened = await store.OpenReadAsync($"{Guid.NewGuid()}.jpg", cancellationToken);

        Assert.Null(opened);
    }

    [Fact]
    public async Task Delete_of_a_missing_blob_is_idempotent()
    {
        LocalFolderSightingPhotoStore store = new(directory);
        string blobName = $"{Guid.NewGuid()}.jpg";

        await store.DeleteAsync(blobName, cancellationToken);
        await store.DeleteAsync(blobName, cancellationToken);

        Assert.Null(await store.OpenReadAsync(blobName, cancellationToken));
    }

    [Theory]
    [MemberData(nameof(InvalidBlobNames))]
    public async Task A_blob_name_that_is_not_a_guid_jpg_is_rejected(string blobName)
    {
        LocalFolderSightingPhotoStore store = new(directory);
        using MemoryStream source = new([1, 2, 3]);

        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(blobName, source, cancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => store.OpenReadAsync(blobName, cancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => store.DeleteAsync(blobName, cancellationToken));
        Assert.False(Directory.Exists(directory));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}

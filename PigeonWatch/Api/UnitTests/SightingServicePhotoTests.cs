using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.Core;
using NSubstitute.ExceptionExtensions;
using PigeonWatch.BusinessLogic.Services;
using PigeonWatch.BusinessObjects;
using PigeonWatch.Data.Repositories;
using PigeonWatch.Data.Storage;

namespace PigeonWatch.UnitTests;

public class SightingServicePhotoTests
{
    private static readonly DateTimeOffset now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);
    private static readonly byte[] normalizedJpeg = [0xFF, 0xD8, 0xFF, 0xDB, 0x00, 0x01, 0xFF, 0xD9];
    private static readonly byte[] thumbnailJpeg = [0xFF, 0xD8, 0x07, 0xFF, 0xD9];

    private readonly ISightingRepository sightingRepository = Substitute.For<ISightingRepository>();
    private readonly IPhotoNormalizationProvider photoNormalizationProvider = Substitute.For<IPhotoNormalizationProvider>();
    private readonly IPhotoThumbnailProvider photoThumbnailProvider = Substitute.For<IPhotoThumbnailProvider>();
    private readonly ISightingPhotoStore sightingPhotoStore = Substitute.For<ISightingPhotoStore>();
    private readonly ILogger<SightingService> logger = Substitute.For<ILogger<SightingService>>();
    private readonly ManualTimeProvider timeProvider = new(now);
    private readonly CancellationToken cancellationToken = TestContext.Current.CancellationToken;
    private readonly List<string> savedBlobNames = [];
    private readonly List<byte[]> savedContents = [];

    public SightingServicePhotoTests()
    {
        sightingRepository
            .CreateAsync(Arg.Any<NewSighting>(), Arg.Any<CancellationToken>())
            .Returns(call => ToSighting(call.Arg<NewSighting>()));
        sightingRepository
            .GetLatestCreateDateByReporterAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((DateTime?)null);
        photoNormalizationProvider
            .NormalizeAsync(Arg.Any<Stream>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(PhotoNormalizationResult.Success(normalizedJpeg));
        photoThumbnailProvider.CreateThumbnail(Arg.Any<byte[]>()).Returns(thumbnailJpeg);
        sightingPhotoStore
            .When(store => store.SaveAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                savedBlobNames.Add(call.ArgAt<string>(0));
                using MemoryStream copy = new();
                call.ArgAt<Stream>(1).CopyTo(copy);
                savedContents.Add(copy.ToArray());
            });
    }

    [Fact]
    public async Task Create_without_a_photo_makes_no_normalization_or_store_calls()
    {
        SightingCreateResult result = await CreateService().CreateAsync(NewSighting(), cancellationToken);

        Assert.True(result.Succeeded);
        Assert.False(result.Sighting!.HasPhoto);
        await sightingRepository.Received(1).CreateAsync(
            Arg.Is<NewSighting>(stored => stored.PhotoBlobName == null),
            Arg.Any<CancellationToken>());
        await AssertNoNormalizationAsync();
        AssertNoStoreCalls();
    }

    [Fact]
    public async Task Create_rejected_by_field_validation_makes_no_normalization_or_store_calls()
    {
        SightingCreateResult result = await CreateService().CreateAsync(
            NewSighting(Photo()) with { Status = SightingStatus.AtTheVet },
            cancellationToken);

        Assert.Equal([SightingErrorCodes.InvalidInitialStatus], result.Errors.Select(error => error.Code));
        await AssertNoNormalizationAsync();
        AssertNoStoreCalls();
        await AssertNoInsertAsync();
    }

    [Fact]
    public async Task Create_blocked_by_the_cooldown_makes_no_normalization_or_store_calls()
    {
        NewSighting sighting = NewSighting(Photo());
        sightingRepository
            .GetLatestCreateDateByReporterAsync(sighting.ReporterId, Arg.Any<CancellationToken>())
            .Returns(now.AddSeconds(-5).UtcDateTime);

        SightingCreateResult result = await CreateService().CreateAsync(sighting, cancellationToken);

        Assert.Equal([SightingErrorCodes.CooldownActive], result.Errors.Select(error => error.Code));
        await AssertNoNormalizationAsync();
        AssertNoStoreCalls();
        await AssertNoInsertAsync();
    }

    [Theory]
    [InlineData(SightingErrorCodes.PhotoTooLarge)]
    [InlineData(SightingErrorCodes.PhotoUnsupportedType)]
    [InlineData(SightingErrorCodes.PhotoUnreadable)]
    public async Task Create_with_a_photo_that_fails_normalization_returns_the_error_and_makes_no_store_calls(string code)
    {
        photoNormalizationProvider
            .NormalizeAsync(Arg.Any<Stream>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(PhotoNormalizationResult.Failure(new SightingError(code, "rejected")));

        SightingCreateResult result = await CreateService().CreateAsync(NewSighting(Photo()), cancellationToken);

        Assert.False(result.Succeeded);
        Assert.Null(result.RetryAfterSeconds);
        Assert.Equal([code], result.Errors.Select(error => error.Code));
        AssertNoStoreCalls();
        await AssertNoInsertAsync();
    }

    [Fact]
    public async Task Create_with_a_photo_stores_the_jpeg_under_a_guid_name_then_its_thumbnail_and_inserts_the_row_with_the_photo_name()
    {
        NewSightingPhoto photo = Photo();

        SightingCreateResult result = await CreateService().CreateAsync(NewSighting(photo), cancellationToken);

        Assert.True(result.Succeeded);
        Assert.True(result.Sighting!.HasPhoto);
        await photoNormalizationProvider.Received(1).NormalizeAsync(photo.Content, photo.Length, Arg.Any<CancellationToken>());
        photoThumbnailProvider.Received(1).CreateThumbnail(normalizedJpeg);
        Assert.Equal(2, savedBlobNames.Count);
        string blobName = savedBlobNames[0];
        Assert.EndsWith(".jpg", blobName, StringComparison.Ordinal);
        Assert.True(Guid.TryParseExact(blobName[..^4], "D", out Guid _));
        Assert.Equal(ThumbnailNameOf(blobName), savedBlobNames[1]);
        Assert.Equal([normalizedJpeg, thumbnailJpeg], savedContents);
        await sightingRepository.Received(1).CreateAsync(
            Arg.Is<NewSighting>(stored => stored.PhotoBlobName == blobName && stored.Photo == null),
            Arg.Any<CancellationToken>());
        await sightingPhotoStore.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_does_not_insert_when_the_store_throws()
    {
        PhotoStorageUnavailableException outage = new("Storage down.", new IOException("Connection reset."));
        sightingPhotoStore
            .SaveAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(outage);

        PhotoStorageUnavailableException thrown = await Assert.ThrowsAsync<PhotoStorageUnavailableException>(
            () => CreateService().CreateAsync(NewSighting(Photo()), cancellationToken));

        Assert.Same(outage, thrown);
        await AssertNoInsertAsync();
        await sightingPhotoStore.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_deletes_the_stored_photo_does_not_insert_and_rethrows_when_the_thumbnail_save_fails()
    {
        PhotoStorageUnavailableException outage = new("Storage down.", new IOException("Connection reset."));
        sightingPhotoStore
            .SaveAsync(Arg.Is<string>(name => name.EndsWith("-thumb.jpg", StringComparison.Ordinal)), Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(outage);

        PhotoStorageUnavailableException thrown = await Assert.ThrowsAsync<PhotoStorageUnavailableException>(
            () => CreateService().CreateAsync(NewSighting(Photo()), cancellationToken));

        Assert.Same(outage, thrown);
        await AssertNoInsertAsync();
        AssertBothBlobsDeleted();
        Assert.Empty(LogCalls());
    }

    [Fact]
    public async Task Create_cancelled_during_the_thumbnail_save_still_deletes_both_blobs_with_an_uncancelled_token()
    {
        using CancellationTokenSource request = new();
        sightingPhotoStore
            .SaveAsync(Arg.Is<string>(name => name.EndsWith("-thumb.jpg", StringComparison.Ordinal)), Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                request.Cancel();

                return Task.FromCanceled(request.Token);
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateService().CreateAsync(NewSighting(Photo()), request.Token));

        await AssertNoInsertAsync();
        string blobName = savedBlobNames[0];
        await sightingPhotoStore.Received(1).DeleteAsync(blobName, Arg.Is<CancellationToken>(token => !token.IsCancellationRequested));
        await sightingPhotoStore.Received(1).DeleteAsync(ThumbnailNameOf(blobName), Arg.Is<CancellationToken>(token => !token.IsCancellationRequested));
    }

    [Fact]
    public async Task Create_deletes_both_blobs_and_rethrows_the_original_exception_when_the_insert_throws()
    {
        InvalidOperationException insertFailure = new("Insert failed.");
        sightingRepository
            .CreateAsync(Arg.Any<NewSighting>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(insertFailure);

        InvalidOperationException thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateService().CreateAsync(NewSighting(Photo()), cancellationToken));

        Assert.Same(insertFailure, thrown);
        AssertBothBlobsDeleted();
        Assert.Empty(LogCalls());
    }

    [Fact]
    public async Task Create_deletes_both_blobs_when_the_reporter_is_not_found()
    {
        sightingRepository
            .CreateAsync(Arg.Any<NewSighting>(), Arg.Any<CancellationToken>())
            .Returns((Sighting?)null);

        SightingCreateResult result = await CreateService().CreateAsync(NewSighting(Photo()), cancellationToken);

        Assert.Equal([SightingErrorCodes.ReporterNotFound], result.Errors.Select(error => error.Code));
        AssertBothBlobsDeleted();
    }

    [Fact]
    public async Task Create_logs_each_failed_delete_at_error_with_its_cause_and_rethrows_the_original_insert_exception()
    {
        InvalidOperationException insertFailure = new("Insert failed.");
        PhotoStorageUnavailableException deleteFailure = new("Delete failed.", new IOException("Connection reset."));
        sightingRepository
            .CreateAsync(Arg.Any<NewSighting>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(insertFailure);
        sightingPhotoStore
            .DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(deleteFailure);

        InvalidOperationException thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateService().CreateAsync(NewSighting(Photo()), cancellationToken));

        Assert.Same(insertFailure, thrown);
        AssertDeleteFailuresLogged(deleteFailure);
    }

    [Fact]
    public async Task Create_logs_each_failed_delete_at_error_with_its_cause_and_keeps_the_reporter_not_found_result()
    {
        PhotoStorageUnavailableException deleteFailure = new("Delete failed.", new IOException("Connection reset."));
        sightingRepository
            .CreateAsync(Arg.Any<NewSighting>(), Arg.Any<CancellationToken>())
            .Returns((Sighting?)null);
        sightingPhotoStore
            .DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(deleteFailure);

        SightingCreateResult result = await CreateService().CreateAsync(NewSighting(Photo()), cancellationToken);

        Assert.Equal([SightingErrorCodes.ReporterNotFound], result.Errors.Select(error => error.Code));
        AssertDeleteFailuresLogged(deleteFailure);
    }

    [Fact]
    public async Task Open_photo_returns_null_without_a_store_call_when_the_sighting_has_no_photo()
    {
        Guid sightingId = Guid.NewGuid();
        sightingRepository.GetPhotoBlobNameAsync(sightingId, Arg.Any<CancellationToken>()).Returns((string?)null);

        Stream? photo = await CreateService().OpenPhotoAsync(sightingId, cancellationToken);

        Assert.Null(photo);
        await sightingPhotoStore.DidNotReceive().OpenReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.Empty(LogCalls());
    }

    [Fact]
    public async Task Open_photo_returns_the_stored_stream()
    {
        Guid sightingId = Guid.NewGuid();
        string blobName = $"{Guid.NewGuid():D}.jpg";
        using MemoryStream stored = new(normalizedJpeg);
        sightingRepository.GetPhotoBlobNameAsync(sightingId, Arg.Any<CancellationToken>()).Returns(blobName);
        sightingPhotoStore.OpenReadAsync(blobName, Arg.Any<CancellationToken>()).Returns(stored);

        Stream? photo = await CreateService().OpenPhotoAsync(sightingId, cancellationToken);

        Assert.Same(stored, photo);
        Assert.Empty(LogCalls());
    }

    [Fact]
    public async Task Open_photo_returns_null_and_logs_a_warning_when_the_blob_is_missing()
    {
        Guid sightingId = Guid.NewGuid();
        string blobName = $"{Guid.NewGuid():D}.jpg";
        sightingRepository.GetPhotoBlobNameAsync(sightingId, Arg.Any<CancellationToken>()).Returns(blobName);
        sightingPhotoStore.OpenReadAsync(blobName, Arg.Any<CancellationToken>()).Returns((Stream?)null);

        Stream? photo = await CreateService().OpenPhotoAsync(sightingId, cancellationToken);

        Assert.Null(photo);
        LogCall call = Assert.Single(LogCalls());
        Assert.Equal(LogLevel.Warning, call.Level);
        Assert.Equal(blobName, call.State["BlobName"]);
        Assert.Equal(sightingId, call.State["SightingId"]);
    }

    [Fact]
    public async Task Open_thumbnail_returns_null_without_a_store_call_when_the_sighting_has_no_photo()
    {
        Guid sightingId = Guid.NewGuid();
        sightingRepository.GetPhotoBlobNameAsync(sightingId, Arg.Any<CancellationToken>()).Returns((string?)null);

        Stream? thumbnail = await CreateService().OpenPhotoThumbnailAsync(sightingId, cancellationToken);

        Assert.Null(thumbnail);
        AssertNoStoreCalls();
        Assert.Empty(LogCalls());
    }

    [Fact]
    public async Task Open_thumbnail_returns_the_stored_thumbnail_without_reading_the_photo_or_resizing()
    {
        Guid sightingId = Guid.NewGuid();
        string blobName = $"{Guid.NewGuid():D}.jpg";
        using MemoryStream stored = new(thumbnailJpeg);
        sightingRepository.GetPhotoBlobNameAsync(sightingId, Arg.Any<CancellationToken>()).Returns(blobName);
        sightingPhotoStore.OpenReadAsync(ThumbnailNameOf(blobName), Arg.Any<CancellationToken>()).Returns(stored);

        Stream? thumbnail = await CreateService().OpenPhotoThumbnailAsync(sightingId, cancellationToken);

        Assert.Same(stored, thumbnail);
        await sightingPhotoStore.DidNotReceive().OpenReadAsync(blobName, Arg.Any<CancellationToken>());
        photoThumbnailProvider.DidNotReceive().CreateThumbnail(Arg.Any<byte[]>());
        Assert.Empty(savedBlobNames);
        Assert.Empty(LogCalls());
    }

    [Fact]
    public async Task Open_thumbnail_creates_a_missing_thumbnail_from_the_photo_stores_it_and_returns_it()
    {
        Guid sightingId = Guid.NewGuid();
        string blobName = $"{Guid.NewGuid():D}.jpg";
        sightingRepository.GetPhotoBlobNameAsync(sightingId, Arg.Any<CancellationToken>()).Returns(blobName);
        sightingPhotoStore.OpenReadAsync(ThumbnailNameOf(blobName), Arg.Any<CancellationToken>()).Returns((Stream?)null);
        sightingPhotoStore.OpenReadAsync(blobName, Arg.Any<CancellationToken>()).Returns(new MemoryStream(normalizedJpeg));

        Stream? thumbnail = await CreateService().OpenPhotoThumbnailAsync(sightingId, cancellationToken);

        Assert.Equal(thumbnailJpeg, ReadAll(thumbnail));
        photoThumbnailProvider.Received(1).CreateThumbnail(Arg.Is<byte[]>(bytes => bytes.SequenceEqual(normalizedJpeg)));
        Assert.Equal([ThumbnailNameOf(blobName)], savedBlobNames);
        Assert.Equal([thumbnailJpeg], savedContents);
        Assert.Empty(LogCalls());
    }

    [Fact]
    public async Task Open_thumbnail_logs_a_failed_save_of_the_created_thumbnail_at_error_with_its_cause_and_still_returns_it()
    {
        Guid sightingId = Guid.NewGuid();
        string blobName = $"{Guid.NewGuid():D}.jpg";
        PhotoStorageUnavailableException saveFailure = new("Save failed.", new IOException("Connection reset."));
        sightingRepository.GetPhotoBlobNameAsync(sightingId, Arg.Any<CancellationToken>()).Returns(blobName);
        sightingPhotoStore.OpenReadAsync(ThumbnailNameOf(blobName), Arg.Any<CancellationToken>()).Returns((Stream?)null);
        sightingPhotoStore.OpenReadAsync(blobName, Arg.Any<CancellationToken>()).Returns(new MemoryStream(normalizedJpeg));
        sightingPhotoStore
            .SaveAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(saveFailure);

        Stream? thumbnail = await CreateService().OpenPhotoThumbnailAsync(sightingId, cancellationToken);

        Assert.Equal(thumbnailJpeg, ReadAll(thumbnail));
        LogCall call = Assert.Single(LogCalls());
        Assert.Equal(LogLevel.Error, call.Level);
        Assert.Same(saveFailure, call.Exception);
        Assert.Equal(ThumbnailNameOf(blobName), call.State["BlobName"]);
        Assert.Equal(sightingId, call.State["SightingId"]);
    }

    [Fact]
    public async Task Open_thumbnail_rethrows_a_storage_outage_while_reading_the_stored_thumbnail()
    {
        Guid sightingId = Guid.NewGuid();
        string blobName = $"{Guid.NewGuid():D}.jpg";
        PhotoStorageUnavailableException outage = new("Read failed.", new IOException("Connection reset."));
        sightingRepository.GetPhotoBlobNameAsync(sightingId, Arg.Any<CancellationToken>()).Returns(blobName);
        sightingPhotoStore.OpenReadAsync(ThumbnailNameOf(blobName), Arg.Any<CancellationToken>()).ThrowsAsync(outage);

        PhotoStorageUnavailableException thrown = await Assert.ThrowsAsync<PhotoStorageUnavailableException>(
            () => CreateService().OpenPhotoThumbnailAsync(sightingId, cancellationToken));

        Assert.Same(outage, thrown);
        photoThumbnailProvider.DidNotReceive().CreateThumbnail(Arg.Any<byte[]>());
        await sightingPhotoStore.DidNotReceive().SaveAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Open_thumbnail_lets_a_save_fault_that_is_not_a_storage_outage_fail_the_request()
    {
        Guid sightingId = Guid.NewGuid();
        string blobName = $"{Guid.NewGuid():D}.jpg";
        ArgumentException saveFault = new("Blob name rejected.", "blobName");
        sightingRepository.GetPhotoBlobNameAsync(sightingId, Arg.Any<CancellationToken>()).Returns(blobName);
        sightingPhotoStore.OpenReadAsync(ThumbnailNameOf(blobName), Arg.Any<CancellationToken>()).Returns((Stream?)null);
        sightingPhotoStore.OpenReadAsync(blobName, Arg.Any<CancellationToken>()).Returns(new MemoryStream(normalizedJpeg));
        sightingPhotoStore
            .SaveAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(saveFault);

        ArgumentException thrown = await Assert.ThrowsAsync<ArgumentException>(
            () => CreateService().OpenPhotoThumbnailAsync(sightingId, cancellationToken));

        Assert.Same(saveFault, thrown);
        Assert.Empty(LogCalls());
    }

    [Fact]
    public async Task Open_thumbnail_returns_null_and_logs_a_warning_when_the_photo_blob_is_missing()
    {
        Guid sightingId = Guid.NewGuid();
        string blobName = $"{Guid.NewGuid():D}.jpg";
        sightingRepository.GetPhotoBlobNameAsync(sightingId, Arg.Any<CancellationToken>()).Returns(blobName);
        sightingPhotoStore.OpenReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((Stream?)null);

        Stream? thumbnail = await CreateService().OpenPhotoThumbnailAsync(sightingId, cancellationToken);

        Assert.Null(thumbnail);
        photoThumbnailProvider.DidNotReceive().CreateThumbnail(Arg.Any<byte[]>());
        Assert.Empty(savedBlobNames);
        LogCall call = Assert.Single(LogCalls());
        Assert.Equal(LogLevel.Warning, call.Level);
        Assert.Equal(blobName, call.State["BlobName"]);
        Assert.Equal(sightingId, call.State["SightingId"]);
    }

    private SightingService CreateService()
    {
        return new(
            sightingRepository,
            new SightingStatusTransitionProvider(),
            new GeoDistanceCalculator(),
            photoNormalizationProvider,
            photoThumbnailProvider,
            sightingPhotoStore,
            timeProvider,
            logger);
    }

    private async Task AssertNoNormalizationAsync()
    {
        await photoNormalizationProvider.DidNotReceive().NormalizeAsync(Arg.Any<Stream>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    private void AssertNoStoreCalls()
    {
        Assert.Empty(sightingPhotoStore.ReceivedCalls());
    }

    private async Task AssertNoInsertAsync()
    {
        await sightingRepository.DidNotReceive().CreateAsync(Arg.Any<NewSighting>(), Arg.Any<CancellationToken>());
    }

    private void AssertBothBlobsDeleted()
    {
        string blobName = savedBlobNames[0];
        sightingPhotoStore.Received(1).DeleteAsync(blobName, Arg.Any<CancellationToken>());
        sightingPhotoStore.Received(1).DeleteAsync(ThumbnailNameOf(blobName), Arg.Any<CancellationToken>());
    }

    private void AssertDeleteFailuresLogged(Exception deleteFailure)
    {
        string blobName = savedBlobNames[0];
        List<LogCall> calls = LogCalls();
        Assert.Equal(2, calls.Count);
        Assert.All(calls, call =>
        {
            Assert.Equal(LogLevel.Error, call.Level);
            Assert.Same(deleteFailure, call.Exception);
            Assert.True(call.State.ContainsKey("TraceId"));
        });
        Assert.Equal(
            new HashSet<object?> { blobName, ThumbnailNameOf(blobName) },
            calls.Select(call => call.State["BlobName"]).ToHashSet());
    }

    private static string ThumbnailNameOf(string blobName)
    {
        return $"{blobName[..^4]}-thumb.jpg";
    }

    private static byte[] ReadAll(Stream? stream)
    {
        Assert.NotNull(stream);
        using MemoryStream copy = new();
        stream.CopyTo(copy);

        return copy.ToArray();
    }

    private List<LogCall> LogCalls()
    {
        List<LogCall> calls = logger.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(ILogger.Log))
            .Select(ToLogCall)
            .ToList();

        return calls;
    }

    private static LogCall ToLogCall(ICall call)
    {
        object?[] arguments = call.GetArguments();
        Dictionary<string, object?> state = [];

        foreach (KeyValuePair<string, object?> pair in (IEnumerable<KeyValuePair<string, object?>>)arguments[2]!)
            state[pair.Key] = pair.Value;

        return new LogCall((LogLevel)arguments[0]!, state, (Exception?)arguments[3]);
    }

    private static NewSightingPhoto Photo()
    {
        byte[] upload = [0x01, 0x02, 0x03];

        return new NewSightingPhoto(new MemoryStream(upload), upload.Length);
    }

    private static NewSighting NewSighting(NewSightingPhoto? photo = null)
    {
        return new(
            Guid.NewGuid(),
            "Grey",
            "Limping near the fountain",
            "Cannot fly",
            52.0,
            21.0,
            SightingStatus.Spotted,
            SightingCriticality.Urgent,
            photo);
    }

    private static Sighting ToSighting(NewSighting sighting)
    {
        return new(
            Guid.NewGuid(),
            sighting.Name,
            sighting.Description,
            sighting.Behaviour,
            sighting.Latitude,
            sighting.Longitude,
            sighting.Status,
            sighting.Criticality,
            "Pidgey",
            now.UtcDateTime,
            sighting.PhotoBlobName is not null);
    }

    private sealed record LogCall(LogLevel Level, Dictionary<string, object?> State, Exception? Exception);
}

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

    private readonly ISightingRepository sightingRepository = Substitute.For<ISightingRepository>();
    private readonly IPhotoNormalizationProvider photoNormalizationProvider = Substitute.For<IPhotoNormalizationProvider>();
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
    public async Task Create_with_a_photo_normalizes_it_stores_the_jpeg_under_a_guid_name_and_inserts_the_row_with_that_name()
    {
        NewSightingPhoto photo = Photo();

        SightingCreateResult result = await CreateService().CreateAsync(NewSighting(photo), cancellationToken);

        Assert.True(result.Succeeded);
        Assert.True(result.Sighting!.HasPhoto);
        await photoNormalizationProvider.Received(1).NormalizeAsync(photo.Content, photo.Length, Arg.Any<CancellationToken>());
        string blobName = Assert.Single(savedBlobNames);
        Assert.EndsWith(".jpg", blobName, StringComparison.Ordinal);
        Assert.True(Guid.TryParseExact(blobName[..^4], "D", out Guid _));
        Assert.Equal(normalizedJpeg, Assert.Single(savedContents));
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
    public async Task Create_deletes_the_blob_and_rethrows_the_original_exception_when_the_insert_throws()
    {
        InvalidOperationException insertFailure = new("Insert failed.");
        sightingRepository
            .CreateAsync(Arg.Any<NewSighting>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(insertFailure);

        InvalidOperationException thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateService().CreateAsync(NewSighting(Photo()), cancellationToken));

        Assert.Same(insertFailure, thrown);
        string blobName = Assert.Single(savedBlobNames);
        await sightingPhotoStore.Received(1).DeleteAsync(blobName, Arg.Any<CancellationToken>());
        Assert.Empty(LogCalls());
    }

    [Fact]
    public async Task Create_deletes_the_blob_when_the_reporter_is_not_found()
    {
        sightingRepository
            .CreateAsync(Arg.Any<NewSighting>(), Arg.Any<CancellationToken>())
            .Returns((Sighting?)null);

        SightingCreateResult result = await CreateService().CreateAsync(NewSighting(Photo()), cancellationToken);

        Assert.Equal([SightingErrorCodes.ReporterNotFound], result.Errors.Select(error => error.Code));
        string blobName = Assert.Single(savedBlobNames);
        await sightingPhotoStore.Received(1).DeleteAsync(blobName, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_logs_a_failed_delete_at_error_with_its_cause_and_rethrows_the_original_insert_exception()
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
        AssertDeleteFailureLogged(deleteFailure, Assert.Single(savedBlobNames));
    }

    [Fact]
    public async Task Create_logs_a_failed_delete_at_error_with_its_cause_and_keeps_the_reporter_not_found_result()
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
        AssertDeleteFailureLogged(deleteFailure, Assert.Single(savedBlobNames));
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

    private SightingService CreateService()
    {
        return new(
            sightingRepository,
            new SightingStatusTransitionProvider(),
            new GeoDistanceCalculator(),
            photoNormalizationProvider,
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

    private void AssertDeleteFailureLogged(Exception deleteFailure, string blobName)
    {
        LogCall call = Assert.Single(LogCalls());
        Assert.Equal(LogLevel.Error, call.Level);
        Assert.Same(deleteFailure, call.Exception);
        Assert.Equal(blobName, call.State["BlobName"]);
        Assert.True(call.State.ContainsKey("TraceId"));
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

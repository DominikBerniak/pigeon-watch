using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PigeonWatch.BusinessObjects;
using PigeonWatch.Data.Repositories;
using PigeonWatch.Data.Storage;

namespace PigeonWatch.BusinessLogic.Services;

public class SightingService(
    ISightingRepository sightingRepository,
    ISightingStatusTransitionProvider sightingStatusTransitionProvider,
    IGeoDistanceCalculator geoDistanceCalculator,
    IPhotoNormalizationProvider photoNormalizationProvider,
    IPhotoThumbnailProvider photoThumbnailProvider,
    ISightingPhotoStore sightingPhotoStore,
    ISubmissionCooldownProvider submissionCooldownProvider,
    ISightingVisibilityProvider sightingVisibilityProvider,
    TimeProvider timeProvider,
    ILogger<SightingService> logger) : ISightingService
{
    private static readonly HashSet<int> listPageSizes =
    [
        SightingRules.ListPageSizeSmall,
        SightingRules.ListPageSizeMedium,
        SightingRules.ListPageSizeLarge
    ];

    public async Task<SightingCreateResult> CreateAsync(NewSighting sighting, CancellationToken cancellationToken = default)
    {
        NewSighting trimmed = sighting with
        {
            Name = string.IsNullOrWhiteSpace(sighting.Name) ? null : sighting.Name.Trim(),
            Description = sighting.Description.Trim(),
            Behaviour = sighting.Behaviour.Trim()
        };

        List<SightingError> errors = Validate(trimmed);

        if (errors.Count > 0)
            return SightingCreateResult.Failure(errors);

        DateTime? latestCreateDate = await sightingRepository.GetLatestCreateDateByReporterAsync(trimmed.ReporterId, cancellationToken);
        int? retryAfterSeconds = submissionCooldownProvider.GetRetryAfterSeconds(latestCreateDate);

        if (retryAfterSeconds is not null)
        {
            return SightingCreateResult.CooldownFailure(
                new SightingError(SightingErrorCodes.CooldownActive, $"Wait {retryAfterSeconds} seconds before reporting another sighting."),
                retryAfterSeconds.Value);
        }

        if (trimmed.Photo is null)
            return await InsertAsync(trimmed, cancellationToken);

        (SightingError? photoError, string? blobName) = await StorePhotoAsync(trimmed.Photo, cancellationToken);

        if (photoError is not null || blobName is null)
            return SightingCreateResult.Failure([photoError ?? UnreadablePhotoError()]);

        bool inserted = false;

        try
        {
            SightingCreateResult result = await InsertAsync(trimmed with { Photo = null, PhotoBlobName = blobName }, cancellationToken);
            inserted = result.Succeeded;

            return result;
        }
        finally
        {
            if (!inserted)
                await DeleteOrphanedPhotoBlobsAsync(blobName, CancellationToken.None);
        }
    }

    public async Task<SightingUpdateResult> UpdateAsync(SightingUpdate update, bool isAdmin, CancellationToken cancellationToken = default)
    {
        SightingUpdate trimmed = update with
        {
            Name = string.IsNullOrWhiteSpace(update.Name) ? null : update.Name.Trim(),
            Description = update.Description.Trim(),
            Behaviour = update.Behaviour.Trim()
        };

        List<SightingError> errors = ValidateUpdate(trimmed);

        if (errors.Count > 0)
            return SightingUpdateResult.Failure(errors);

        Sighting? current = await sightingRepository.GetByIdAsync(trimmed.SightingId, cancellationToken);

        if (current is null || !sightingVisibilityProvider.CanView(ToAccess(current), trimmed.UserId, isAdmin))
            return SightingUpdateResult.Failure([SightingNotFoundError()]);

        if (current.ReporterId != trimmed.UserId)
            return SightingUpdateResult.Failure([new SightingError(SightingErrorCodes.NotSightingOwner, "Only the reporter can edit this sighting.")]);

        if (sightingVisibilityProvider.IsRemoved(ToAccess(current)))
            return SightingUpdateResult.Failure([SightingRemovedError()]);

        if (trimmed.Status != current.Status && !sightingStatusTransitionProvider.IsTransitionAllowed(current.Status, trimmed.Status))
        {
            return SightingUpdateResult.Failure(
                [new SightingError(SightingErrorCodes.InvalidStatusTransition, $"The status cannot change from {current.Status} to {trimmed.Status}.")]);
        }

        bool? engaged = null;

        if (!string.Equals(trimmed.Description, current.Description, StringComparison.Ordinal))
        {
            engaged = await sightingRepository.HasOtherUserEngagementAsync(current.Id, current.ReporterId, cancellationToken);

            if (engaged.Value)
            {
                return SightingUpdateResult.Failure(
                    [new SightingError(SightingErrorCodes.DescriptionLocked, "The description cannot change once another user has commented or made contact.")]);
            }
        }

        if (trimmed.Photo is null)
        {
            SightingPhotoChange photoChange = trimmed.RemovePhoto ? SightingPhotoChange.Remove : SightingPhotoChange.Keep;
            SightingUpdateOutcome? saved = await sightingRepository.UpdateAsync(current.Id, trimmed.UserId, trimmed, photoChange, null, cancellationToken);

            if (saved is null)
                return SightingUpdateResult.Failure([SightingNotFoundError()]);

            return await CompleteUpdateAsync(saved, trimmed.UserId, engaged, cancellationToken);
        }

        (SightingError? photoError, string? newBlobName) = await StorePhotoAsync(trimmed.Photo, cancellationToken);

        if (photoError is not null || newBlobName is null)
            return SightingUpdateResult.Failure([photoError ?? UnreadablePhotoError()]);

        SightingUpdateOutcome? updated = null;

        try
        {
            updated = await sightingRepository.UpdateAsync(
                current.Id,
                trimmed.UserId,
                trimmed with { Photo = null },
                SightingPhotoChange.Replace,
                newBlobName,
                cancellationToken);
        }
        finally
        {
            if (updated is null)
                await DeleteOrphanedPhotoBlobsAsync(newBlobName, CancellationToken.None);
        }

        if (updated is null)
            return SightingUpdateResult.Failure([SightingNotFoundError()]);

        return await CompleteUpdateAsync(updated, trimmed.UserId, engaged, cancellationToken);
    }

    public async Task<SightingRemovalResult> RemoveAsync(Guid sightingId, Guid userId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        if (!isAdmin)
            return SightingRemovalResult.Failure([NotAdminError()]);

        Sighting? removed = await sightingRepository.RemoveAsync(sightingId, userId, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);

        if (removed is null)
            return SightingRemovalResult.Failure([SightingNotFoundError()]);

        return SightingRemovalResult.Success(removed);
    }

    public async Task<SightingRemovalResult> RestoreAsync(Guid sightingId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        if (!isAdmin)
            return SightingRemovalResult.Failure([NotAdminError()]);

        Sighting? restored = await sightingRepository.RestoreAsync(sightingId, cancellationToken);

        if (restored is null)
            return SightingRemovalResult.Failure([SightingNotFoundError()]);

        return SightingRemovalResult.Success(restored);
    }

    public async Task<Stream?> OpenPhotoAsync(Guid sightingId, Guid userId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        if (!await CanViewAsync(sightingId, userId, isAdmin, cancellationToken))
            return null;

        string? blobName = await sightingRepository.GetPhotoBlobNameAsync(sightingId, cancellationToken);

        if (blobName is null)
            return null;

        Stream? photo = await sightingPhotoStore.OpenReadAsync(blobName, cancellationToken);

        if (photo is null)
            logger.LogWarning("Photo blob {BlobName} of sighting {SightingId} is missing from photo storage", blobName, sightingId);

        return photo;
    }

    public async Task<Stream?> OpenPhotoThumbnailAsync(Guid sightingId, Guid userId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        if (!await CanViewAsync(sightingId, userId, isAdmin, cancellationToken))
            return null;

        string? blobName = await sightingRepository.GetPhotoBlobNameAsync(sightingId, cancellationToken);

        if (blobName is null)
            return null;

        string thumbnailBlobName = GetThumbnailBlobName(blobName);
        Stream? storedThumbnail = await sightingPhotoStore.OpenReadAsync(thumbnailBlobName, cancellationToken);

        if (storedThumbnail is not null)
            return storedThumbnail;

        byte[]? photo = await ReadPhotoAsync(blobName, cancellationToken);

        if (photo is null)
        {
            logger.LogWarning("Photo blob {BlobName} of sighting {SightingId} is missing from photo storage", blobName, sightingId);

            return null;
        }

        byte[] thumbnail = photoThumbnailProvider.CreateThumbnail(photo);
        await SaveCreatedThumbnailAsync(thumbnailBlobName, thumbnail, sightingId, cancellationToken);

        return new MemoryStream(thumbnail, writable: false);
    }

    public async Task<Sighting?> GetAsync(Guid id, Guid userId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        Sighting? sighting = await sightingRepository.GetByIdAsync(id, cancellationToken);

        if (sighting is null || !sightingVisibilityProvider.CanView(ToAccess(sighting), userId, isAdmin))
            return null;

        return sighting;
    }

    public async Task<SightingEditPermissions?> GetEditPermissionsAsync(Sighting sighting, Guid userId, CancellationToken cancellationToken = default)
    {
        if (sighting.ReporterId != userId || sightingVisibilityProvider.IsRemoved(ToAccess(sighting)))
            return null;

        bool engaged = await sightingRepository.HasOtherUserEngagementAsync(sighting.Id, sighting.ReporterId, cancellationToken);

        return new SightingEditPermissions(!engaged, sightingStatusTransitionProvider.GetAllowedNextStatuses(sighting.Status));
    }

    public async Task<SightingNearbyResult> GetNearbyAsync(
        double latitude,
        double longitude,
        IReadOnlyCollection<SightingStatus> statuses,
        IReadOnlyCollection<SightingCriticality> criticalities,
        bool isAdmin,
        CancellationToken cancellationToken = default)
    {
        List<SightingStatus> openStatuses = Enum.GetValues<SightingStatus>()
            .Where(status => !sightingStatusTransitionProvider.IsResolved(status))
            .ToList();
        List<SightingError> errors = ValidateNearbyFilters(statuses, criticalities, openStatuses);

        if (errors.Count > 0)
            return SightingNearbyResult.Failure(errors);

        GeoBoundingBox box = geoDistanceCalculator.GetBoundingBox(latitude, longitude, SightingRules.NearbyRadiusKilometers);
        List<SightingStatus> requestedStatuses = statuses.Count == 0 ? openStatuses : statuses.Distinct().ToList();
        List<SightingCriticality> requestedCriticalities = criticalities.Distinct().ToList();

        IReadOnlyList<Sighting> candidates = await sightingRepository.ListInBoxAsync(
            box.MinLatitude,
            box.MaxLatitude,
            box.MinLongitude,
            box.MaxLongitude,
            requestedStatuses,
            requestedCriticalities,
            SightingRules.NearbyBoxMaxRows,
            isAdmin,
            cancellationToken);

        List<Sighting> nearby = candidates
            .Where(candidate => geoDistanceCalculator.GetDistanceKilometers(latitude, longitude, candidate.Latitude, candidate.Longitude) <= SightingRules.NearbyRadiusKilometers)
            .OrderByDescending(candidate => candidate.CreatedAt)
            .Take(SightingRules.NearbyMaxResults)
            .ToList();

        return SightingNearbyResult.Success(nearby);
    }

    public async Task<SightingListResult> GetListPageAsync(SightingListQuery query, bool isAdmin, CancellationToken cancellationToken = default)
    {
        List<SightingError> errors = ValidateListQuery(query);

        if (errors.Count > 0)
            return SightingListResult.Failure(errors);

        List<SightingStatus> requestedStatuses = query.Statuses.Count == 0
            ? Enum.GetValues<SightingStatus>().Where(status => !sightingStatusTransitionProvider.IsResolved(status)).ToList()
            : query.Statuses.Distinct().ToList();
        List<SightingCriticality> requestedCriticalities = query.Criticalities.Distinct().ToList();
        GeoRadiusFilter radius = geoDistanceCalculator.GetRadiusFilter(query.Latitude, query.Longitude, SightingRules.NearbyRadiusKilometers);
        int skip = (query.Page - 1) * query.PageSize;

        SightingPage page = await sightingRepository.ListPageInRadiusAsync(
            radius,
            requestedStatuses,
            requestedCriticalities,
            skip,
            query.PageSize,
            isAdmin,
            cancellationToken);

        return SightingListResult.Success(page);
    }

    public async Task<IReadOnlyList<Sighting>> GetMineAsync(Guid reporterId, CancellationToken cancellationToken = default)
    {
        return await sightingRepository.ListByReporterAsync(reporterId, SightingRules.MineMaxResults, cancellationToken);
    }

    private async Task<SightingCreateResult> InsertAsync(NewSighting sighting, CancellationToken cancellationToken = default)
    {
        Sighting? created = await sightingRepository.CreateAsync(sighting, cancellationToken);

        if (created is null)
            return SightingCreateResult.Failure([new SightingError(SightingErrorCodes.ReporterNotFound, "The reporting user does not exist.")]);

        SightingEditPermissions permissions = new(true, sightingStatusTransitionProvider.GetAllowedNextStatuses(created.Status));

        return SightingCreateResult.Success(created, permissions);
    }

    private async Task<SightingUpdateResult> CompleteUpdateAsync(
        SightingUpdateOutcome outcome,
        Guid userId,
        bool? engaged,
        CancellationToken cancellationToken = default)
    {
        if (outcome.ReplacedPhotoBlobName is not null)
            await DeleteReplacedPhotoBlobsAsync(outcome.ReplacedPhotoBlobName, outcome.Sighting.Id, CancellationToken.None);

        Sighting sighting = outcome.Sighting;
        SightingEditPermissions? permissions = engaged is null
            ? await GetEditPermissionsAsync(sighting, userId, cancellationToken)
            : new SightingEditPermissions(!engaged.Value, sightingStatusTransitionProvider.GetAllowedNextStatuses(sighting.Status));

        return SightingUpdateResult.Success(sighting, permissions);
    }

    private async Task<(SightingError? Error, string? BlobName)> StorePhotoAsync(NewSightingPhoto photo, CancellationToken cancellationToken = default)
    {
        PhotoNormalizationResult normalized = await photoNormalizationProvider.NormalizeAsync(photo.Content, photo.Length, cancellationToken);

        if (!normalized.Succeeded || normalized.Jpeg is null)
            return (normalized.Error ?? UnreadablePhotoError(), null);

        byte[] thumbnail = photoThumbnailProvider.CreateThumbnail(normalized.Jpeg);
        string blobName = $"{Guid.NewGuid():D}{SightingRules.PhotoBlobExtension}";

        using (MemoryStream jpeg = new(normalized.Jpeg, writable: false))
            await sightingPhotoStore.SaveAsync(blobName, jpeg, cancellationToken);

        bool stored = false;

        try
        {
            using (MemoryStream thumbnailJpeg = new(thumbnail, writable: false))
                await sightingPhotoStore.SaveAsync(GetThumbnailBlobName(blobName), thumbnailJpeg, cancellationToken);

            stored = true;
        }
        finally
        {
            if (!stored)
                await DeleteOrphanedPhotoBlobsAsync(blobName, CancellationToken.None);
        }

        return (null, blobName);
    }

    private async Task DeleteOrphanedPhotoBlobsAsync(string blobName, CancellationToken cancellationToken = default)
    {
        await DeleteOrphanedPhotoAsync(GetThumbnailBlobName(blobName), cancellationToken);
        await DeleteOrphanedPhotoAsync(blobName, cancellationToken);
    }

    private async Task DeleteReplacedPhotoBlobsAsync(string blobName, Guid sightingId, CancellationToken cancellationToken = default)
    {
        await DeleteReplacedPhotoAsync(GetThumbnailBlobName(blobName), sightingId, cancellationToken);
        await DeleteReplacedPhotoAsync(blobName, sightingId, cancellationToken);
    }

    private async Task DeleteReplacedPhotoAsync(string blobName, Guid sightingId, CancellationToken cancellationToken = default)
    {
        try
        {
            await sightingPhotoStore.DeleteAsync(blobName, cancellationToken);
        }
        catch (Exception exception)
        {
            string traceId = Activity.Current?.TraceId.ToString() ?? "none";
            logger.LogError(
                exception,
                "Replaced photo blob {BlobName} of sighting {SightingId} could not be deleted after the sighting was saved (trace {TraceId}); the blob is left orphaned",
                blobName,
                sightingId,
                traceId);
        }
    }

    private static SightingError SightingNotFoundError()
    {
        return new SightingError(SightingErrorCodes.SightingNotFound, "The sighting does not exist.");
    }

    private static SightingError SightingRemovedError()
    {
        return new SightingError(SightingErrorCodes.SightingRemoved, "The sighting was removed by an admin and cannot be changed.");
    }

    private static SightingAccess ToAccess(Sighting sighting)
    {
        return new SightingAccess(sighting.ReporterId, sighting.RemovedAt);
    }

    private async Task<bool> CanViewAsync(Guid sightingId, Guid userId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        SightingAccess? access = await sightingRepository.GetAccessAsync(sightingId, cancellationToken);

        return access is not null && sightingVisibilityProvider.CanView(access, userId, isAdmin);
    }

    private static SightingError NotAdminError()
    {
        return new SightingError(SightingErrorCodes.NotAdmin, "Only an admin can remove or restore a sighting.");
    }

    private static SightingError UnreadablePhotoError()
    {
        return new SightingError(SightingErrorCodes.PhotoUnreadable, "The photo could not be read.");
    }

    private static List<SightingError> ValidateUpdate(SightingUpdate update)
    {
        List<SightingError> errors = [];

        if (!Enum.IsDefined(update.Status))
            errors.Add(new SightingError(SightingErrorCodes.InvalidStatus, "Status must be a defined sighting status."));

        if (!Enum.IsDefined(update.Criticality))
            errors.Add(new SightingError(SightingErrorCodes.InvalidCriticality, "Criticality must be Monitor, Needs help soon or Urgent."));

        if (update.Description.Length == 0)
            errors.Add(new SightingError(SightingErrorCodes.DescriptionRequired, "A description is required."));

        if (update.Photo is not null && update.RemovePhoto)
            errors.Add(new SightingError(SightingErrorCodes.PhotoConflict, "Send either a new photo or removePhoto, not both."));

        return errors;
    }

    private async Task DeleteOrphanedPhotoAsync(string blobName, CancellationToken cancellationToken = default)
    {
        try
        {
            await sightingPhotoStore.DeleteAsync(blobName, cancellationToken);
        }
        catch (Exception exception)
        {
            string traceId = Activity.Current?.TraceId.ToString() ?? "none";
            logger.LogError(
                exception,
                "Photo blob {BlobName} could not be deleted after the sighting was not saved (trace {TraceId}); the blob is left orphaned",
                blobName,
                traceId);
        }
    }

    private async Task<byte[]?> ReadPhotoAsync(string blobName, CancellationToken cancellationToken = default)
    {
        Stream? stored = await sightingPhotoStore.OpenReadAsync(blobName, cancellationToken);

        if (stored is null)
            return null;

        await using (stored)
        {
            using MemoryStream buffer = new();
            await stored.CopyToAsync(buffer, cancellationToken);

            return buffer.ToArray();
        }
    }

    private async Task SaveCreatedThumbnailAsync(string thumbnailBlobName, byte[] thumbnail, Guid sightingId, CancellationToken cancellationToken = default)
    {
        try
        {
            using MemoryStream jpeg = new(thumbnail, writable: false);
            await sightingPhotoStore.SaveAsync(thumbnailBlobName, jpeg, cancellationToken);
        }
        catch (PhotoStorageUnavailableException exception)
        {
            logger.LogError(
                exception,
                "Thumbnail blob {BlobName} of sighting {SightingId} was created but could not be stored; it is served without being stored and will be created again on the next request",
                thumbnailBlobName,
                sightingId);
        }
    }

    private static string GetThumbnailBlobName(string blobName)
    {
        string stem = blobName.EndsWith(SightingRules.PhotoBlobExtension, StringComparison.Ordinal)
            ? blobName[..^SightingRules.PhotoBlobExtension.Length]
            : blobName;

        return $"{stem}{SightingRules.PhotoThumbnailBlobSuffix}{SightingRules.PhotoBlobExtension}";
    }

    private List<SightingError> Validate(NewSighting sighting)
    {
        List<SightingError> errors = [];

        if (!sightingStatusTransitionProvider.IsValidInitialStatus(sighting.Status))
            errors.Add(new SightingError(SightingErrorCodes.InvalidInitialStatus, "A new sighting must be Spotted or Caught."));

        if (!Enum.IsDefined(sighting.Criticality))
            errors.Add(new SightingError(SightingErrorCodes.InvalidCriticality, "Criticality must be Monitor, Needs help soon or Urgent."));

        if (!IsValidLocation(sighting.Latitude, sighting.Longitude))
            errors.Add(new SightingError(SightingErrorCodes.InvalidLocation, "Latitude must be between -90 and 90 and longitude between -180 and 180."));

        return errors;
    }

    private static List<SightingError> ValidateNearbyFilters(
        IReadOnlyCollection<SightingStatus> statuses,
        IReadOnlyCollection<SightingCriticality> criticalities,
        IReadOnlyCollection<SightingStatus> openStatuses)
    {
        List<SightingError> errors = [];

        if (statuses.Any(status => !openStatuses.Contains(status)))
            errors.Add(new SightingError(SightingErrorCodes.InvalidFilterStatus, "Status filter accepts only Spotted, Caught, At the vet, In home care or Awaiting adoption."));

        if (criticalities.Any(criticality => !Enum.IsDefined(criticality)))
            errors.Add(new SightingError(SightingErrorCodes.InvalidFilterCriticality, "Criticality filter accepts only Monitor, Needs help soon or Urgent."));

        return errors;
    }

    private static List<SightingError> ValidateListQuery(SightingListQuery query)
    {
        List<SightingError> errors = [];

        if (query.Statuses.Any(status => !Enum.IsDefined(status)))
            errors.Add(new SightingError(SightingErrorCodes.InvalidFilterStatus, "Status filter accepts only defined sighting statuses."));

        if (query.Criticalities.Any(criticality => !Enum.IsDefined(criticality)))
            errors.Add(new SightingError(SightingErrorCodes.InvalidFilterCriticality, "Criticality filter accepts only Monitor, Needs help soon or Urgent."));

        if (query.Page < 1)
            errors.Add(new SightingError(SightingErrorCodes.InvalidPage, "Page must be 1 or greater."));
        else if (listPageSizes.Contains(query.PageSize) && query.Page > int.MaxValue / query.PageSize)
            errors.Add(new SightingError(SightingErrorCodes.InvalidPage, "Page is too large for the page size."));

        if (!listPageSizes.Contains(query.PageSize))
            errors.Add(new SightingError(SightingErrorCodes.InvalidPageSize, "Page size must be 10, 20 or 30."));

        return errors;
    }

    private static bool IsValidLocation(double latitude, double longitude)
    {
        return double.IsFinite(latitude)
            && double.IsFinite(longitude)
            && latitude >= -90
            && latitude <= 90
            && longitude >= -180
            && longitude <= 180;
    }
}

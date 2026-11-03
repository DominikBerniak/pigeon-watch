using PigeonWatch.BusinessObjects;

namespace PigeonWatch.BusinessLogic.Services;

public interface IPhotoNormalizationProvider
{
    Task<PhotoNormalizationResult> NormalizeAsync(Stream input, long length, CancellationToken cancellationToken = default);
}

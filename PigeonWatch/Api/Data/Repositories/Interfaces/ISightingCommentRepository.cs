using PigeonWatch.BusinessObjects;

namespace PigeonWatch.Data.Repositories;

public interface ISightingCommentRepository
{
    Task<SightingComment?> CreateAsync(NewSightingComment comment, CancellationToken cancellationToken = default);
    Task<SightingComment?> GetAsync(Guid sightingId, Guid commentId, CancellationToken cancellationToken = default);
    Task<SightingCommentPage> ListPageAsync(Guid sightingId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<SightingComment?> UpdateTextAsync(Guid sightingId, Guid commentId, Guid authorId, string text, DateTime editedAt, CancellationToken cancellationToken = default);
    Task<SightingComment?> SoftDeleteAsync(Guid sightingId, Guid commentId, Guid authorId, DateTime deletedAt, CancellationToken cancellationToken = default);
    Task<DateTime?> GetLatestSubmissionDateByAuthorAsync(Guid authorId, CancellationToken cancellationToken = default);
    Task<bool> SightingExistsAsync(Guid sightingId, CancellationToken cancellationToken = default);
}

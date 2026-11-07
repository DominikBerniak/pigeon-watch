using PigeonWatch.BusinessObjects;

namespace PigeonWatch.BusinessLogic.Services;

public interface ISightingCommentService
{
    Task<SightingCommentPageResult> ListAsync(Guid sightingId, int page, CancellationToken cancellationToken = default);
    Task<SightingCommentResult> CreateAsync(Guid sightingId, Guid authorId, string text, CancellationToken cancellationToken = default);
    Task<SightingCommentResult> UpdateAsync(Guid sightingId, Guid commentId, Guid userId, string text, CancellationToken cancellationToken = default);
    Task<SightingCommentResult> DeleteAsync(Guid sightingId, Guid commentId, Guid userId, CancellationToken cancellationToken = default);
}

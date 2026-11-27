using PigeonWatch.BusinessObjects;

namespace PigeonWatch.BusinessLogic.Services;

public interface ISightingCommentService
{
    Task<SightingCommentPageResult> ListAsync(Guid sightingId, Guid userId, bool isAdmin, int page, CancellationToken cancellationToken = default);
    Task<SightingCommentResult> CreateAsync(Guid sightingId, Guid authorId, bool isAdmin, string text, CancellationToken cancellationToken = default);
    Task<SightingCommentResult> UpdateAsync(Guid sightingId, Guid commentId, Guid userId, bool isAdmin, string text, CancellationToken cancellationToken = default);
    Task<SightingCommentResult> DeleteAsync(Guid sightingId, Guid commentId, Guid userId, bool isAdmin, CancellationToken cancellationToken = default);
}

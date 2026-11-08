using PigeonWatch.BusinessObjects;

namespace PigeonWatch.Data.Repositories;

public interface ISightingThreadRepository
{
    Task<Guid?> GetSightingOwnerAsync(Guid sightingId, CancellationToken cancellationToken = default);
    Task<SightingThread?> FindThreadAsync(Guid sightingId, Guid participantId, CancellationToken cancellationToken = default);
    Task<SightingThread?> GetThreadForUserAsync(Guid sightingId, Guid threadId, Guid userId, CancellationToken cancellationToken = default);
    Task<bool> IsThreadVisibleToUserAsync(Guid sightingId, Guid threadId, Guid userId, CancellationToken cancellationToken = default);
    Task<(SightingThread Thread, SightingMessage Message)?> StartOrAppendAsync(Guid sightingId, Guid participantId, string text, DateTime now, CancellationToken cancellationToken = default);
    Task<SightingMessage?> AppendAsync(Guid sightingId, Guid threadId, Guid senderId, string text, DateTime now, CancellationToken cancellationToken = default);
    Task<SightingThreadPage> ListThreadsForSightingAsync(Guid sightingId, Guid userId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<SightingThreadPage> ListThreadsForUserAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<SightingMessagePage> ListMessagesAsync(Guid sightingId, Guid threadId, Guid userId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<bool> MarkReadAsync(Guid sightingId, Guid threadId, Guid userId, DateTime seenAt, DateTime now, CancellationToken cancellationToken = default);
    Task<int> CountUnreadMessagesAsync(Guid userId, CancellationToken cancellationToken = default);
}

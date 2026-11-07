using PigeonWatch.BusinessObjects;

namespace PigeonWatch.BusinessLogic.Services;

public interface ISightingThreadService
{
    Task<SightingThreadResult> StartAsync(Guid sightingId, Guid userId, string text, CancellationToken cancellationToken = default);
    Task<SightingThreadResult> ReplyAsync(Guid sightingId, Guid threadId, Guid userId, string text, CancellationToken cancellationToken = default);
    Task<SightingThreadPageResult> ListForSightingAsync(Guid sightingId, Guid userId, int page, CancellationToken cancellationToken = default);
    Task<SightingThreadPageResult> ListForUserAsync(Guid userId, int page, CancellationToken cancellationToken = default);
    Task<SightingMessagePageResult> ListMessagesAsync(Guid sightingId, Guid threadId, Guid userId, int page, CancellationToken cancellationToken = default);
}

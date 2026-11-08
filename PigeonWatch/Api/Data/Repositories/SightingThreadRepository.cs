using System.Linq.Expressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PigeonWatch.BusinessObjects;
using PigeonWatch.Data.Entities;
using PigeonWatch.Data.Mappers;

namespace PigeonWatch.Data.Repositories;

public class SightingThreadRepository(PigeonWatchDbContext db, ISightingThreadMapper sightingThreadMapper) : ISightingThreadRepository
{
    private const int duplicateKeyRow = 2601;
    private const int uniqueConstraintViolation = 2627;
    private const string threadPairIndexName = "IX_SIGHTING_THREAD_SIGHTING_ID_PARTICIPANT_ID";

    public async Task<Guid?> GetSightingOwnerAsync(Guid sightingId, CancellationToken cancellationToken = default)
    {
        Guid? ownerId = await db.Sightings
            .Where(e => e.Id == sightingId)
            .Select(e => (Guid?)e.ReporterId)
            .FirstOrDefaultAsync(cancellationToken);

        return ownerId;
    }

    public async Task<SightingThread?> FindThreadAsync(Guid sightingId, Guid participantId, CancellationToken cancellationToken = default)
    {
        SightingThreadEntity? entity = await ThreadsWithNames()
            .FirstOrDefaultAsync(e => e.SightingId == sightingId && e.ParticipantId == participantId, cancellationToken);

        if (entity is null)
            return null;

        return await ToThreadForUserAsync(entity, participantId, cancellationToken);
    }

    public async Task<SightingThread?> GetThreadForUserAsync(Guid sightingId, Guid threadId, Guid userId, CancellationToken cancellationToken = default)
    {
        SightingThreadEntity? entity = await ThreadsWithNames()
            .FirstOrDefaultAsync(
                e => e.Id == threadId && e.SightingId == sightingId && (e.ParticipantId == userId || e.Sighting.ReporterId == userId),
                cancellationToken);

        if (entity is null)
            return null;

        return await ToThreadForUserAsync(entity, userId, cancellationToken);
    }

    public async Task<(SightingThread Thread, SightingMessage Message)?> StartOrAppendAsync(Guid sightingId, Guid participantId, string text, DateTime now, CancellationToken cancellationToken = default)
    {
        UserAccountEntity? participant = await db.UserAccounts.FindAsync([participantId], cancellationToken);

        if (participant is null)
            return null;

        SightingEntity? sighting = await db.Sightings
            .Include(e => e.Reporter)
            .FirstOrDefaultAsync(e => e.Id == sightingId, cancellationToken);

        if (sighting is null)
            return null;

        SightingThreadEntity? existing = await FindTrackedThreadAsync(sightingId, participantId, cancellationToken);

        if (existing is not null)
            return await AppendToThreadAsync(existing, participant, text, now, cancellationToken);

        SightingThreadEntity thread = new()
        {
            SightingId = sightingId,
            Sighting = sighting,
            ParticipantId = participantId,
            Participant = participant,
            LastMessageAt = now,
            ParticipantLastReadAt = now
        };
        db.SightingThreads.Add(thread);
        SightingMessageEntity message = AddMessage(thread, participant, text);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDuplicateThread(exception))
        {
            db.Entry(message).State = EntityState.Detached;
            db.Entry(thread).State = EntityState.Detached;
            SightingThreadEntity? winner = await FindTrackedThreadAsync(sightingId, participantId, cancellationToken);

            if (winner is null)
                throw;

            return await AppendToThreadAsync(winner, participant, text, now, cancellationToken);
        }

        return (sightingThreadMapper.ToThread(thread, false), sightingThreadMapper.ToMessage(message));
    }

    public async Task<SightingMessage?> AppendAsync(Guid sightingId, Guid threadId, Guid senderId, string text, DateTime now, CancellationToken cancellationToken = default)
    {
        UserAccountEntity? sender = await db.UserAccounts.FindAsync([senderId], cancellationToken);

        if (sender is null)
            return null;

        SightingThreadEntity? thread = await FindTrackedThreadForUserAsync(sightingId, threadId, senderId, cancellationToken);

        if (thread is null)
            return null;

        thread.LastMessageAt = now;
        MarkSideRead(thread, senderId, now);
        SightingMessageEntity message = AddMessage(thread, sender, text);
        await db.SaveChangesAsync(cancellationToken);

        return sightingThreadMapper.ToMessage(message);
    }

    public async Task<SightingThreadPage> ListThreadsForSightingAsync(Guid sightingId, Guid userId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        IQueryable<SightingThreadEntity> query = db.SightingThreads
            .AsNoTracking()
            .Where(e => e.SightingId == sightingId && (e.Sighting.ReporterId == userId || e.ParticipantId == userId));

        return await ListThreadPageAsync(query, userId, page, pageSize, cancellationToken);
    }

    public async Task<SightingThreadPage> ListThreadsForUserAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        IQueryable<SightingThreadEntity> query = db.SightingThreads
            .AsNoTracking()
            .Where(e => e.ParticipantId == userId || e.Sighting.ReporterId == userId);

        return await ListThreadPageAsync(query, userId, page, pageSize, cancellationToken);
    }

    public async Task<SightingMessagePage> ListMessagesAsync(Guid sightingId, Guid threadId, Guid userId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        IQueryable<SightingMessageEntity> query = db.SightingMessages
            .AsNoTracking()
            .Where(e => e.ThreadId == threadId
                && e.Thread.SightingId == sightingId
                && (e.Thread.ParticipantId == userId || e.Thread.Sighting.ReporterId == userId));

        int totalCount = await query.CountAsync(cancellationToken);
        long skip = ((long)page - 1) * pageSize;

        if (skip >= totalCount)
            return new SightingMessagePage([], totalCount);

        List<SightingMessageEntity> entities = await query
            .OrderByDescending(e => e.CreateDate)
            .ThenByDescending(e => e.Id)
            .Skip((int)skip)
            .Take(pageSize)
            .Include(e => e.Sender)
            .ToListAsync(cancellationToken);

        return new SightingMessagePage(entities.Select(sightingThreadMapper.ToMessage).ToList(), totalCount);
    }

    public async Task<bool> MarkReadAsync(Guid sightingId, Guid threadId, Guid userId, DateTime now, CancellationToken cancellationToken = default)
    {
        SightingThreadEntity? thread = await FindTrackedThreadForUserAsync(sightingId, threadId, userId, cancellationToken);

        if (thread is null)
            return false;

        MarkSideRead(thread, userId, now);
        await db.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task<int> CountUnreadMessagesAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        int count = await db.SightingMessages
            .AsNoTracking()
            .Where(m => m.SenderId != userId
                && (m.Thread.ParticipantId == userId
                    ? m.Thread.ParticipantLastReadAt == null || m.CreateDate > m.Thread.ParticipantLastReadAt
                    : m.Thread.Sighting.ReporterId == userId
                        && (m.Thread.OwnerLastReadAt == null || m.CreateDate > m.Thread.OwnerLastReadAt)))
            .CountAsync(cancellationToken);

        return count;
    }

    private async Task<SightingThreadPage> ListThreadPageAsync(IQueryable<SightingThreadEntity> query, Guid userId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        int totalCount = await query.CountAsync(cancellationToken);
        long skip = ((long)page - 1) * pageSize;

        if (skip >= totalCount)
            return new SightingThreadPage([], totalCount);

        List<SightingThreadEntity> entities = await query
            .OrderByDescending(e => e.LastMessageAt)
            .ThenByDescending(e => e.Id)
            .Skip((int)skip)
            .Take(pageSize)
            .Include(e => e.Sighting)
            .ThenInclude(e => e.Reporter)
            .Include(e => e.Participant)
            .ToListAsync(cancellationToken);

        return new SightingThreadPage(await ToThreadsForUserAsync(entities, userId, cancellationToken), totalCount);
    }

    private async Task<(SightingThread Thread, SightingMessage Message)?> AppendToThreadAsync(SightingThreadEntity thread, UserAccountEntity sender, string text, DateTime now, CancellationToken cancellationToken = default)
    {
        thread.LastMessageAt = now;
        MarkSideRead(thread, sender.Id, now);
        SightingMessageEntity message = AddMessage(thread, sender, text);
        await db.SaveChangesAsync(cancellationToken);

        return (sightingThreadMapper.ToThread(thread, false), sightingThreadMapper.ToMessage(message));
    }

    private async Task<SightingThread> ToThreadForUserAsync(SightingThreadEntity entity, Guid userId, CancellationToken cancellationToken = default)
    {
        List<SightingThread> threads = await ToThreadsForUserAsync([entity], userId, cancellationToken);

        return threads[0];
    }

    private async Task<List<SightingThread>> ToThreadsForUserAsync(List<SightingThreadEntity> entities, Guid userId, CancellationToken cancellationToken = default)
    {
        if (entities.Count == 0)
            return [];

        List<Guid> threadIds = entities.Select(e => e.Id).ToList();
        List<Guid> unreadIds = await db.SightingThreads
            .AsNoTracking()
            .Where(e => threadIds.Contains(e.Id))
            .Where(IsUnreadFor(userId))
            .Select(e => e.Id)
            .ToListAsync(cancellationToken);
        HashSet<Guid> unread = unreadIds.ToHashSet();

        return entities.Select(e => sightingThreadMapper.ToThread(e, unread.Contains(e.Id))).ToList();
    }

    private Task<SightingThreadEntity?> FindTrackedThreadForUserAsync(Guid sightingId, Guid threadId, Guid userId, CancellationToken cancellationToken = default)
    {
        return db.SightingThreads
            .FirstOrDefaultAsync(
                e => e.Id == threadId && e.SightingId == sightingId && (e.ParticipantId == userId || e.Sighting.ReporterId == userId),
                cancellationToken);
    }

    private static void MarkSideRead(SightingThreadEntity thread, Guid userId, DateTime now)
    {
        if (thread.ParticipantId == userId)
            thread.ParticipantLastReadAt = now;
        else
            thread.OwnerLastReadAt = now;
    }

    private static Expression<Func<SightingThreadEntity, bool>> IsUnreadFor(Guid userId)
    {
        return e => e.Messages.Any(m => m.SenderId != userId
            && (e.ParticipantId == userId
                ? e.ParticipantLastReadAt == null || m.CreateDate > e.ParticipantLastReadAt
                : e.OwnerLastReadAt == null || m.CreateDate > e.OwnerLastReadAt));
    }

    private SightingMessageEntity AddMessage(SightingThreadEntity thread, UserAccountEntity sender, string text)
    {
        SightingMessageEntity message = sightingThreadMapper.ToMessageEntity(new NewSightingMessage(thread.Id, sender.Id, text));
        message.Thread = thread;
        message.Sender = sender;
        db.SightingMessages.Add(message);

        return message;
    }

    private Task<SightingThreadEntity?> FindTrackedThreadAsync(Guid sightingId, Guid participantId, CancellationToken cancellationToken = default)
    {
        return db.SightingThreads
            .Include(e => e.Sighting)
            .ThenInclude(e => e.Reporter)
            .Include(e => e.Participant)
            .FirstOrDefaultAsync(e => e.SightingId == sightingId && e.ParticipantId == participantId, cancellationToken);
    }

    private IQueryable<SightingThreadEntity> ThreadsWithNames()
    {
        return db.SightingThreads
            .AsNoTracking()
            .Include(e => e.Sighting)
            .ThenInclude(e => e.Reporter)
            .Include(e => e.Participant);
    }

    private static bool IsDuplicateThread(DbUpdateException exception)
    {
        return exception.InnerException is SqlException { Number: duplicateKeyRow or uniqueConstraintViolation } sqlException
            && sqlException.Message.Contains(threadPairIndexName, StringComparison.Ordinal);
    }
}

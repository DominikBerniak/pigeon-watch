using Microsoft.EntityFrameworkCore;
using PigeonWatch.BusinessObjects;
using PigeonWatch.Data.Entities;
using PigeonWatch.Data.Mappers;

namespace PigeonWatch.Data.Repositories;

public class SightingCommentRepository(PigeonWatchDbContext db, ISightingCommentMapper sightingCommentMapper) : ISightingCommentRepository
{
    public async Task<SightingComment?> CreateAsync(NewSightingComment comment, CancellationToken cancellationToken = default)
    {
        UserAccountEntity? author = await db.UserAccounts.FindAsync([comment.AuthorId], cancellationToken);

        if (author is null)
            return null;

        SightingCommentEntity entity = sightingCommentMapper.ToEntity(comment);
        entity.Author = author;
        db.SightingComments.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        return sightingCommentMapper.ToComment(entity);
    }

    public async Task<SightingComment?> GetAsync(Guid sightingId, Guid commentId, CancellationToken cancellationToken = default)
    {
        SightingCommentEntity? entity = await db.SightingComments
            .AsNoTracking()
            .Include(e => e.Author)
            .FirstOrDefaultAsync(e => e.Id == commentId && e.SightingId == sightingId, cancellationToken);

        return entity is null ? null : sightingCommentMapper.ToComment(entity);
    }

    public async Task<SightingCommentPage> ListPageAsync(Guid sightingId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        IQueryable<SightingCommentEntity> query = db.SightingComments
            .AsNoTracking()
            .Where(e => e.SightingId == sightingId);

        int totalCount = await query.CountAsync(cancellationToken);
        List<SightingCommentEntity> entities = await query
            .OrderByDescending(e => e.CreateDate)
            .ThenByDescending(e => e.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(e => e.Author)
            .ToListAsync(cancellationToken);

        return new SightingCommentPage(entities.Select(sightingCommentMapper.ToComment).ToList(), totalCount);
    }

    public async Task<SightingComment?> UpdateTextAsync(Guid commentId, string text, DateTime editedAt, CancellationToken cancellationToken = default)
    {
        SightingCommentEntity? entity = await db.SightingComments
            .Include(e => e.Author)
            .FirstOrDefaultAsync(e => e.Id == commentId, cancellationToken);

        if (entity is null)
            return null;

        entity.Text = text;
        entity.EditedAt = editedAt;
        await db.SaveChangesAsync(cancellationToken);

        return sightingCommentMapper.ToComment(entity);
    }

    public async Task<SightingComment?> SoftDeleteAsync(Guid commentId, DateTime deletedAt, CancellationToken cancellationToken = default)
    {
        SightingCommentEntity? entity = await db.SightingComments
            .Include(e => e.Author)
            .FirstOrDefaultAsync(e => e.Id == commentId, cancellationToken);

        if (entity is null)
            return null;

        entity.DeletedAt = deletedAt;
        await db.SaveChangesAsync(cancellationToken);

        return sightingCommentMapper.ToComment(entity);
    }

    public async Task<DateTime?> GetLatestSubmissionDateByAuthorAsync(Guid authorId, CancellationToken cancellationToken = default)
    {
        DateTime? latestCreated = await db.SightingComments
            .Where(e => e.AuthorId == authorId)
            .OrderByDescending(e => e.CreateDate)
            .Select(e => (DateTime?)e.CreateDate)
            .FirstOrDefaultAsync(cancellationToken);
        DateTime? latestEdited = await db.SightingComments
            .Where(e => e.AuthorId == authorId && e.EditedAt != null)
            .OrderByDescending(e => e.EditedAt)
            .Select(e => e.EditedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (latestCreated is null)
            return null;

        DateTime latest = latestEdited is not null && latestEdited.Value > latestCreated.Value ? latestEdited.Value : latestCreated.Value;

        return DateTime.SpecifyKind(latest, DateTimeKind.Utc);
    }

    public async Task<bool> SightingExistsAsync(Guid sightingId, CancellationToken cancellationToken = default)
    {
        bool exists = await db.Sightings.AnyAsync(e => e.Id == sightingId, cancellationToken);

        return exists;
    }
}

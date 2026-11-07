using PigeonWatch.BusinessObjects;
using PigeonWatch.Data.Entities;

namespace PigeonWatch.Data.Mappers;

public class SightingCommentMapper : ISightingCommentMapper
{
    SightingCommentEntity ISightingCommentMapper.ToEntity(NewSightingComment comment)
    {
        return new()
        {
            SightingId = comment.SightingId,
            AuthorId = comment.AuthorId,
            Text = comment.Text
        };
    }

    SightingComment ISightingCommentMapper.ToComment(SightingCommentEntity entity)
    {
        return new SightingComment(
            entity.Id,
            entity.SightingId,
            entity.AuthorId,
            entity.Author.DisplayName,
            entity.Text,
            DateTime.SpecifyKind(entity.CreateDate, DateTimeKind.Utc),
            entity.EditedAt is null ? null : DateTime.SpecifyKind(entity.EditedAt.Value, DateTimeKind.Utc),
            entity.DeletedAt is null ? null : DateTime.SpecifyKind(entity.DeletedAt.Value, DateTimeKind.Utc));
    }
}

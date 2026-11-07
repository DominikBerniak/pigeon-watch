using PigeonWatch.BusinessObjects;
using PigeonWatch.Data.Entities;

namespace PigeonWatch.Data.Mappers;

public interface ISightingCommentMapper
{
    internal SightingCommentEntity ToEntity(NewSightingComment comment);
    internal SightingComment ToComment(SightingCommentEntity entity);
}

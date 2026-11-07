using PigeonWatch.BusinessObjects;
using PigeonWatch.Data.Entities;

namespace PigeonWatch.Data.Mappers;

public interface ISightingThreadMapper
{
    internal SightingMessageEntity ToMessageEntity(NewSightingMessage message);
    internal SightingThread ToThread(SightingThreadEntity entity);
    internal SightingMessage ToMessage(SightingMessageEntity entity);
}

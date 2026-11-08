using PigeonWatch.BusinessObjects;
using PigeonWatch.Data.Entities;

namespace PigeonWatch.Data.Mappers;

public class SightingThreadMapper : ISightingThreadMapper
{
    SightingMessageEntity ISightingThreadMapper.ToMessageEntity(NewSightingMessage message)
    {
        return new()
        {
            ThreadId = message.ThreadId,
            SenderId = message.SenderId,
            Text = message.Text
        };
    }

    SightingThread ISightingThreadMapper.ToThread(SightingThreadEntity entity, bool hasUnread)
    {
        return new SightingThread(
            entity.Id,
            entity.SightingId,
            entity.Sighting.Name,
            entity.Sighting.ReporterId,
            entity.Sighting.Reporter.DisplayName,
            entity.ParticipantId,
            entity.Participant.DisplayName,
            DateTime.SpecifyKind(entity.LastMessageAt, DateTimeKind.Utc),
            hasUnread);
    }

    SightingMessage ISightingThreadMapper.ToMessage(SightingMessageEntity entity)
    {
        return new SightingMessage(
            entity.Id,
            entity.ThreadId,
            entity.SenderId,
            entity.Sender.DisplayName,
            entity.Text,
            DateTime.SpecifyKind(entity.CreateDate, DateTimeKind.Utc));
    }
}

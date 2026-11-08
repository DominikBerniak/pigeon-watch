using PigeonWatch.BusinessObjects;
using PigeonWatch.WebApi.Models;

namespace PigeonWatch.WebApi.ViewModelCreators;

public class SightingThreadViewModelCreator : ISightingThreadViewModelCreator
{
    public SightingThreadModel CreateThread(SightingThread thread, Guid currentUserId)
    {
        bool isOwner = thread.OwnerId == currentUserId;

        return new()
        {
            Id = thread.Id,
            SightingId = thread.SightingId,
            SightingName = thread.SightingName,
            CounterpartDisplayName = isOwner ? thread.ParticipantDisplayName : thread.OwnerDisplayName,
            IsOwner = isOwner,
            LastMessageAt = thread.LastMessageAt,
            HasUnread = thread.HasUnread
        };
    }

    public SightingThreadPageModel CreateThreadPage(SightingThreadPage page, Guid currentUserId)
    {
        return new()
        {
            Items = page.Items.Select(thread => CreateThread(thread, currentUserId)).ToList(),
            TotalCount = page.TotalCount
        };
    }

    public SightingMessageModel CreateMessage(SightingMessage message, Guid currentUserId)
    {
        return new()
        {
            Id = message.Id,
            Text = message.Text,
            SenderDisplayName = message.SenderDisplayName,
            CreatedAt = message.CreatedAt,
            IsMine = message.SenderId == currentUserId
        };
    }

    public SightingMessagePageModel CreateMessagePage(SightingMessagePage page, Guid currentUserId)
    {
        return new()
        {
            Items = page.Items.Select(message => CreateMessage(message, currentUserId)).ToList(),
            TotalCount = page.TotalCount
        };
    }

    public UnreadMessageCountModel CreateUnreadCount(int count)
    {
        return new()
        {
            Count = count
        };
    }

    public SightingThreadStartModel CreateStart(SightingThread thread, SightingMessage message, Guid currentUserId)
    {
        return new()
        {
            Thread = CreateThread(thread, currentUserId),
            Message = CreateMessage(message, currentUserId)
        };
    }
}

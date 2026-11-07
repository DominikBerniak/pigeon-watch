using PigeonWatch.BusinessObjects;
using PigeonWatch.WebApi.Models;

namespace PigeonWatch.WebApi.ViewModelCreators;

public interface ISightingThreadViewModelCreator
{
    SightingThreadModel CreateThread(SightingThread thread, Guid currentUserId);
    SightingThreadPageModel CreateThreadPage(SightingThreadPage page, Guid currentUserId);
    SightingMessageModel CreateMessage(SightingMessage message, Guid currentUserId);
    SightingMessagePageModel CreateMessagePage(SightingMessagePage page, Guid currentUserId);
    SightingThreadStartModel CreateStart(SightingThread thread, SightingMessage message, Guid currentUserId);
}

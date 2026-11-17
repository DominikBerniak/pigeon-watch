using PigeonWatch.BusinessObjects;

namespace PigeonWatch.BusinessLogic.Services;

public interface ISightingVisibilityProvider
{
    bool CanView(SightingAccess access, Guid viewerId, bool viewerIsAdmin);
    bool IsRemoved(SightingAccess access);
}

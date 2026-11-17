using PigeonWatch.BusinessObjects;

namespace PigeonWatch.BusinessLogic.Services;

public class SightingVisibilityProvider : ISightingVisibilityProvider
{
    public bool CanView(SightingAccess access, Guid viewerId, bool viewerIsAdmin)
    {
        return access.RemovedAt is null || viewerId == access.ReporterId || viewerIsAdmin;
    }

    public bool IsRemoved(SightingAccess access)
    {
        return access.RemovedAt is not null;
    }
}

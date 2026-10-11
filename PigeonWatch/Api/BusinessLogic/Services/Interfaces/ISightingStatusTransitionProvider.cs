using PigeonWatch.BusinessObjects;

namespace PigeonWatch.BusinessLogic.Services;

public interface ISightingStatusTransitionProvider
{
    IReadOnlyList<SightingStatus> GetAllowedNextStatuses(SightingStatus status);
    bool IsTransitionAllowed(SightingStatus from, SightingStatus to);
    bool IsResolved(SightingStatus status);
    bool IsValidInitialStatus(SightingStatus status);
    int GetCriticalityRank(SightingCriticality criticality);
}

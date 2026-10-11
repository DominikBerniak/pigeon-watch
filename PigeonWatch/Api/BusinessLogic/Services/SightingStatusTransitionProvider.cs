using PigeonWatch.BusinessObjects;

namespace PigeonWatch.BusinessLogic.Services;

public class SightingStatusTransitionProvider : ISightingStatusTransitionProvider
{
    private static readonly IReadOnlyDictionary<SightingStatus, IReadOnlyList<SightingStatus>> transitions =
        new Dictionary<SightingStatus, IReadOnlyList<SightingStatus>>
        {
            [SightingStatus.Spotted] = [SightingStatus.Caught, SightingStatus.NotFound],
            [SightingStatus.Caught] = [SightingStatus.AtTheVet, SightingStatus.InHomeCare, SightingStatus.Released],
            [SightingStatus.AtTheVet] = [SightingStatus.InHomeCare, SightingStatus.AwaitingAdoption, SightingStatus.Released, SightingStatus.PassedAway],
            [SightingStatus.InHomeCare] = [SightingStatus.AtTheVet, SightingStatus.AwaitingAdoption, SightingStatus.Released],
            [SightingStatus.AwaitingAdoption] = [SightingStatus.Adopted, SightingStatus.AtTheVet],
            [SightingStatus.Adopted] = [],
            [SightingStatus.Released] = [],
            [SightingStatus.PassedAway] = [],
            [SightingStatus.NotFound] = []
        };

    private static readonly IReadOnlySet<SightingStatus> resolvedStatuses = new HashSet<SightingStatus>
    {
        SightingStatus.Adopted,
        SightingStatus.Released,
        SightingStatus.PassedAway,
        SightingStatus.NotFound
    };

    private static readonly IReadOnlySet<SightingStatus> initialStatuses = new HashSet<SightingStatus>
    {
        SightingStatus.Spotted,
        SightingStatus.Caught
    };

    private static readonly IReadOnlyDictionary<SightingCriticality, int> criticalityRanks = new Dictionary<SightingCriticality, int>
    {
        [SightingCriticality.Monitor] = 1,
        [SightingCriticality.NeedsHelpSoon] = 2,
        [SightingCriticality.Urgent] = 3
    };

    public IReadOnlyList<SightingStatus> GetAllowedNextStatuses(SightingStatus status)
    {
        return transitions.TryGetValue(status, out IReadOnlyList<SightingStatus>? next) ? next : [];
    }

    public bool IsTransitionAllowed(SightingStatus from, SightingStatus to)
    {
        return GetAllowedNextStatuses(from).Contains(to);
    }

    public bool IsResolved(SightingStatus status)
    {
        return resolvedStatuses.Contains(status);
    }

    public bool IsValidInitialStatus(SightingStatus status)
    {
        return initialStatuses.Contains(status);
    }

    public int GetCriticalityRank(SightingCriticality criticality)
    {
        if (criticalityRanks.TryGetValue(criticality, out int rank))
            return rank;

        throw new ArgumentOutOfRangeException(nameof(criticality), criticality, "Unknown sighting criticality.");
    }
}

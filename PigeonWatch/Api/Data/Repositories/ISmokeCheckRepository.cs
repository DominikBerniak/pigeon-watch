using PigeonWatch.BusinessObjects;

namespace PigeonWatch.Data.Repositories;

public interface ISmokeCheckRepository
{
    Task<SmokeCheckResult> InsertReadBackAndRollBackAsync(CancellationToken cancellationToken = default);
}

using PigeonWatch.BusinessObjects;
using PigeonWatch.Data.Repositories;

namespace PigeonWatch.BusinessLogic.Services;

public class DatabaseHealthService(ISmokeCheckRepository smokeCheckRepository) : IDatabaseHealthService
{
    public async Task<DatabaseHealthResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            SmokeCheckResult smokeCheck = await smokeCheckRepository.InsertReadBackAndRollBackAsync(cancellationToken);
            return DatabaseHealthResult.Healthy(smokeCheck);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return DatabaseHealthResult.Unhealthy();
        }
    }
}

using Microsoft.Extensions.Logging;
using PigeonWatch.BusinessObjects;
using PigeonWatch.Data.Repositories;

namespace PigeonWatch.BusinessLogic.Services;

public class DatabaseHealthService(
    ISmokeCheckRepository smokeCheckRepository,
    ILogger<DatabaseHealthService> logger) : IDatabaseHealthService
{
    public async Task<DatabaseHealthResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            SmokeCheckResult smokeCheck = await smokeCheckRepository.InsertReadBackAndRollBackAsync(cancellationToken);
            return DatabaseHealthResult.Healthy(smokeCheck);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Database health check failed");
            return DatabaseHealthResult.Unhealthy();
        }
    }
}

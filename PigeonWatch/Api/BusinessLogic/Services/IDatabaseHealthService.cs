using PigeonWatch.BusinessObjects;

namespace PigeonWatch.BusinessLogic.Services;

public interface IDatabaseHealthService
{
    Task<DatabaseHealthResult> CheckAsync(CancellationToken cancellationToken = default);
}

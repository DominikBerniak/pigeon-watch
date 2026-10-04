using PigeonWatch.BusinessObjects;

namespace PigeonWatch.BusinessLogic.Services;

public interface IClientConfigurationService
{
    Task<ClientConfiguration> GetAsync(CancellationToken cancellationToken = default);
}

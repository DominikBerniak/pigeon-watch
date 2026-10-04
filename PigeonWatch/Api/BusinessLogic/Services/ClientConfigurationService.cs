using PigeonWatch.BusinessObjects;

namespace PigeonWatch.BusinessLogic.Services;

public class ClientConfigurationService : IClientConfigurationService
{
    public Task<ClientConfiguration> GetAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new ClientConfiguration(
            AccountRules.PasswordMinLength,
            AccountRules.PasswordRequiresDigit,
            AccountRules.PasswordRequiresLowercase,
            AccountRules.PasswordRequiresUppercase,
            AccountRules.PasswordRequiresNonAlphanumeric,
            AccountRules.DisplayNameMinLength,
            AccountRules.DisplayNameMaxLength));
}

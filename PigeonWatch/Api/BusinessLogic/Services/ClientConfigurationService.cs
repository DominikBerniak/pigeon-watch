using PigeonWatch.BusinessObjects;

namespace PigeonWatch.BusinessLogic.Services;

public class ClientConfigurationService : IClientConfigurationService
{
    public Task<ClientConfiguration> GetAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ClientConfiguration(
            AccountRules.PasswordMinLength,
            AccountRules.PasswordRequiresDigit,
            AccountRules.PasswordRequiresLowercase,
            AccountRules.PasswordRequiresUppercase,
            AccountRules.PasswordRequiresNonAlphanumeric,
            AccountRules.DisplayNameMinLength,
            AccountRules.DisplayNameMaxLength,
            SightingRules.NameMaxLength,
            SightingRules.DescriptionMaxLength,
            SightingRules.BehaviourMaxLength,
            SightingRules.CooldownSeconds,
            SightingRules.NearbyRadiusKilometers,
            SightingRules.PhotoMaxBytes,
            SightingRules.PhotoMaxDimension,
            SightingRules.PhotoAllowedContentTypes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            SightingCommentRules.TextMaxLength,
            SightingRules.CooldownSeconds,
            SightingCommentRules.PageSize,
            SightingMessageRules.TextMaxLength,
            SightingMessageRules.PageSize));
    }
}

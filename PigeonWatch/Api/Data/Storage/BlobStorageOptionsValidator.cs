using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace PigeonWatch.Data.Storage;

internal sealed class BlobStorageOptionsValidator(IHostEnvironment environment) : IValidateOptions<BlobStorageOptions>
{
    public ValidateOptionsResult Validate(string? name, BlobStorageOptions options)
    {
        bool hasLocalDirectory = !string.IsNullOrWhiteSpace(options.LocalDirectory);
        bool hasBlobService = options.ServiceUri is not null && !string.IsNullOrWhiteSpace(options.ContainerName);

        if (environment.IsDevelopment())
        {
            if (hasLocalDirectory || hasBlobService)
                return ValidateOptionsResult.Success;

            return ValidateOptionsResult.Fail(
                $"{BlobStorageOptions.SectionName}: in Development set either {nameof(BlobStorageOptions.LocalDirectory)} or both {nameof(BlobStorageOptions.ServiceUri)} and {nameof(BlobStorageOptions.ContainerName)}.");
        }

        List<string> failures = [];

        if (!hasBlobService)
            failures.Add($"{BlobStorageOptions.SectionName}: {nameof(BlobStorageOptions.ServiceUri)} and {nameof(BlobStorageOptions.ContainerName)} are required in the {environment.EnvironmentName} environment.");

        if (hasLocalDirectory)
            failures.Add($"{BlobStorageOptions.SectionName}: {nameof(BlobStorageOptions.LocalDirectory)} is allowed only in Development and must be empty in the {environment.EnvironmentName} environment.");

        if (failures.Count > 0)
            return ValidateOptionsResult.Fail(failures);

        return ValidateOptionsResult.Success;
    }
}

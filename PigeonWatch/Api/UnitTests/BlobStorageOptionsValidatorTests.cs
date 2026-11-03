using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
using PigeonWatch.Data.Storage;

namespace PigeonWatch.UnitTests;

public class BlobStorageOptionsValidatorTests
{
    private static readonly Uri serviceUri = new("https://pigeonwatchstorage.blob.core.windows.net");
    private const string containerName = "sighting-photos";
    private const string localDirectory = "LocalPhotos";

    public static TheoryData<string, Uri?, string?, string?, bool> Matrix => new()
    {
        { Environments.Development, null, null, localDirectory, true },
        { Environments.Development, serviceUri, containerName, null, true },
        { Environments.Development, serviceUri, containerName, localDirectory, true },
        { Environments.Development, null, null, null, false },
        { Environments.Development, serviceUri, null, null, false },
        { Environments.Development, null, containerName, null, false },
        { Environments.Development, null, null, "   ", false },
        { Environments.Production, serviceUri, containerName, null, true },
        { Environments.Production, null, null, null, false },
        { Environments.Production, serviceUri, null, null, false },
        { Environments.Production, null, containerName, null, false },
        { Environments.Production, null, null, localDirectory, false },
        { Environments.Production, serviceUri, containerName, localDirectory, false },
        { Environments.Staging, serviceUri, containerName, null, true },
        { Environments.Staging, serviceUri, containerName, localDirectory, false }
    };

    [Theory]
    [MemberData(nameof(Matrix))]
    public void Validation_follows_the_environment_matrix(string environmentName, Uri? uri, string? container, string? directory, bool expectedValid)
    {
        IHostEnvironment environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(environmentName);
        BlobStorageOptionsValidator validator = new(environment);
        BlobStorageOptions options = new() { ServiceUri = uri, ContainerName = container, LocalDirectory = directory };

        ValidateOptionsResult result = validator.Validate(Options.DefaultName, options);

        Assert.Equal(expectedValid, result.Succeeded);

        if (!expectedValid)
            Assert.Contains(BlobStorageOptions.SectionName, result.FailureMessage, StringComparison.Ordinal);
    }
}

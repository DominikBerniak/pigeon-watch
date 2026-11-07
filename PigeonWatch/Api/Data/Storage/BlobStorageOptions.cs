namespace PigeonWatch.Data.Storage;

public class BlobStorageOptions
{
    public const string SectionName = "BlobStorage";

    public Uri? ServiceUri { get; set; }

    public string? ContainerName { get; set; }

    public string? LocalDirectory { get; set; }
}

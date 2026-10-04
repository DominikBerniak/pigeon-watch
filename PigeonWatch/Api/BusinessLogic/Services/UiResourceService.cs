using System.Collections;
using System.Globalization;
using System.Resources;
using PigeonWatch.BusinessObjects;

namespace PigeonWatch.BusinessLogic.Services;

public class UiResourceService : IUiResourceService
{
    private static readonly ResourceManager resourceManager = new(UiResources.BaseName, typeof(UiResources).Assembly);

    public Task<UiLabelSet> GetAsync(string culture, CancellationToken cancellationToken = default)
    {
        CultureInfo? specificCulture = ResolveSpecificCulture(culture);
        ResourceSet defaultLabels = resourceManager.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: false)
            ?? throw new MissingManifestResourceException($"The {UiResources.BaseName} resources are not embedded.");
        SortedDictionary<string, string> labels = new(StringComparer.Ordinal);

        foreach (DictionaryEntry entry in defaultLabels)
        {
            string key = (string)entry.Key;
            string defaultValue = entry.Value as string ?? string.Empty;
            labels[key] = specificCulture is null ? defaultValue : resourceManager.GetString(key, specificCulture) ?? defaultValue;
        }

        return Task.FromResult(new UiLabelSet(specificCulture?.Name ?? UiResources.DefaultCulture, labels));
    }

    private static CultureInfo? ResolveSpecificCulture(string culture)
    {
        CultureInfo cultureInfo;

        try
        {
            cultureInfo = CultureInfo.GetCultureInfo(culture);
        }
        catch (CultureNotFoundException)
        {
            return null;
        }

        if (string.IsNullOrEmpty(cultureInfo.Name) || string.Equals(cultureInfo.Name, UiResources.DefaultCulture, StringComparison.OrdinalIgnoreCase))
            return null;

        ResourceSet? cultureLabels = resourceManager.GetResourceSet(cultureInfo, createIfNotExists: true, tryParents: false);

        return cultureLabels is null ? null : cultureInfo;
    }
}

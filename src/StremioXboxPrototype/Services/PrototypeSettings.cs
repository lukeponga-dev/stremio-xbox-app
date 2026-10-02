using System.Text.Json;
using StremioXboxPrototype.Models;
using Windows.Storage;

namespace StremioXboxPrototype.Services;

public static class PrototypeSettings
{
    private const string StreamAddonsKey = "StreamAddons";
    private const string LibraryKey = "Library";
    private const string StreamingServiceUrlKey = "StreamingServiceUrl";

    public static IReadOnlyList<AddonEndpoint> GetStreamAddons()
    {
        var raw = ApplicationData.Current.LocalSettings.Values[StreamAddonsKey] as string;
        if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<AddonEndpoint>();

        return raw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => Uri.TryCreate(line, UriKind.Absolute, out var uri) &&
                           uri.Scheme == Uri.UriSchemeHttps)
            .Select((line, index) => new AddonEndpoint($"Configured add-on {index + 1}", new Uri(line)))
            .ToList();
    }

    public static string GetStreamAddonText() =>
        ApplicationData.Current.LocalSettings.Values[StreamAddonsKey] as string ?? "";

    public static void SetStreamAddonText(string value) =>
        ApplicationData.Current.LocalSettings.Values[StreamAddonsKey] = value.Trim();

    public static void SetStreamAddons(IEnumerable<AddonEndpoint> addons) =>
        SetStreamAddonText(string.Join(Environment.NewLine, addons.Select(addon => addon.ManifestUri.AbsoluteUri)));

    public static string GetStreamingServiceUrlText() =>
        ApplicationData.Current.LocalSettings.Values[StreamingServiceUrlKey] as string ?? "";

    public static Uri? GetStreamingServiceUrl()
    {
        var value = GetStreamingServiceUrlText().Trim();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) return null;
        return new Uri(uri.AbsoluteUri.TrimEnd('/') + "/");
    }

    public static void SetStreamingServiceUrl(Uri uri) =>
        ApplicationData.Current.LocalSettings.Values[StreamingServiceUrlKey] = uri.AbsoluteUri.TrimEnd('/') + "/";

    public static IReadOnlyList<MetaItem> GetLibrary()
    {
        var raw = ApplicationData.Current.LocalSettings.Values[LibraryKey] as string;
        if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<MetaItem>();
        try
        {
            return JsonSerializer.Deserialize(raw, StremioJsonContext.Default.ListMetaItem) ?? new List<MetaItem>();
        }
        catch
        {
            return Array.Empty<MetaItem>();
        }
    }

    public static bool IsInLibrary(string id) => GetLibrary().Any(item => item.Id == id);

    public static bool ToggleLibrary(MetaItem item)
    {
        var library = GetLibrary().ToList();
        var existing = library.FindIndex(value => value.Id == item.Id);
        var added = existing < 0;
        if (added) library.Insert(0, item);
        else library.RemoveAt(existing);
        ApplicationData.Current.LocalSettings.Values[LibraryKey] =
            JsonSerializer.Serialize(library, StremioJsonContext.Default.ListMetaItem);
        return added;
    }
}

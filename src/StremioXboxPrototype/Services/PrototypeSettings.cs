using System.Text.Json;
using StremioXboxPrototype.Models;
using Windows.Storage;

namespace StremioXboxPrototype.Services;

public static class PrototypeSettings
{
    public const string DefaultStreamingServiceUrl = "http://192.168.1.103:32768/";
    private const string StreamAddonsKey = "StreamAddons";
    private const string LibraryKey = "Library";
    private const string StreamingServiceUrlKey = "StreamingServiceUrl";
    private const string ProfileEmailKey = "ProfileEmail";
    private const string ProfileIdKey = "ProfileId";
    private const string ProfileAvatarKey = "ProfileAvatar";
    private const string ProfileAddonCountKey = "ProfileAddonCount";

    public static AccountProfileCache? GetProfileCache()
    {
        var email = ApplicationData.Current.LocalSettings.Values[ProfileEmailKey] as string;
        if (string.IsNullOrWhiteSpace(email)) return null;
        var id = ApplicationData.Current.LocalSettings.Values[ProfileIdKey] as string ?? "";
        var avatar = ApplicationData.Current.LocalSettings.Values[ProfileAvatarKey] as string;
        var count = ApplicationData.Current.LocalSettings.Values[ProfileAddonCountKey] as int? ?? 0;
        return new AccountProfileCache(email, id, avatar, count);
    }

    public static void SaveProfileCache(AccountProfileCache profile)
    {
        ApplicationData.Current.LocalSettings.Values[ProfileEmailKey] = profile.Email;
        ApplicationData.Current.LocalSettings.Values[ProfileIdKey] = profile.Id;
        ApplicationData.Current.LocalSettings.Values[ProfileAvatarKey] = profile.Avatar ?? "";
        ApplicationData.Current.LocalSettings.Values[ProfileAddonCountKey] = profile.AddonCount;
    }

    public static void ClearProfileCache()
    {
        foreach (var key in new[] { ProfileEmailKey, ProfileIdKey, ProfileAvatarKey, ProfileAddonCountKey })
            ApplicationData.Current.LocalSettings.Values.Remove(key);
    }

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
        ApplicationData.Current.LocalSettings.Values[StreamingServiceUrlKey] as string ?? DefaultStreamingServiceUrl;

    public static Uri? GetStreamingServiceUrl()
    {
        var value = GetStreamingServiceUrlText().Trim();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) return null;
        return new Uri(uri.AbsoluteUri.TrimEnd('/') + "/");
    }

    public static void SetStreamingServiceUrl(Uri uri) =>
        ApplicationData.Current.LocalSettings.Values[StreamingServiceUrlKey] = uri.AbsoluteUri.TrimEnd('/') + "/";

    public static void ClearStreamingServiceUrl() =>
        ApplicationData.Current.LocalSettings.Values[StreamingServiceUrlKey] = "";

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

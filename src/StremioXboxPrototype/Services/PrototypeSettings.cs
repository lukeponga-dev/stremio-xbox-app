using System.Text.Json;
using StremioXboxPrototype.Models;
using Windows.Storage;

namespace StremioXboxPrototype.Services;

public static class PrototypeSettings
{
    public const string DefaultStreamingServiceUrl = "http://192.168.1.105:11470/";
    private const string PreviousHostedStreamingServiceUrl = "https://watchstream-stremio-server.onrender.com/";
    private const string LegacyStreamingServiceUrl = "http://192.168.1.103:32768/";
    private const string StreamAddonsKey = "StreamAddons";
    private const string LibraryKey = "Library";
    private const string StreamingServiceUrlKey = "StreamingServiceUrl";
    private const string PosterAnimationsKey = "PosterAnimations";

    public static bool GetPosterAnimationsEnabled() =>
        ApplicationData.Current.LocalSettings.Values[PosterAnimationsKey] as bool? ?? true;

    public static void SetPosterAnimationsEnabled(bool enabled) =>
        ApplicationData.Current.LocalSettings.Values[PosterAnimationsKey] = enabled;
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

    public static string GetStreamingServiceUrlText()
    {
        var saved = ApplicationData.Current.LocalSettings.Values[StreamingServiceUrlKey] as string;
        if (saved is null) return DefaultStreamingServiceUrl;

        // Migrate previous defaults while preserving custom servers and an explicit disconnect.
        var normalized = saved.Trim().TrimEnd('/') + "/";
        if (string.Equals(normalized, LegacyStreamingServiceUrl, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, PreviousHostedStreamingServiceUrl, StringComparison.OrdinalIgnoreCase))
        {
            ApplicationData.Current.LocalSettings.Values[StreamingServiceUrlKey] = DefaultStreamingServiceUrl;
            return DefaultStreamingServiceUrl;
        }

        return saved;
    }

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

    private static ApplicationDataContainer WatchHistory =>
        ApplicationData.Current.LocalSettings.CreateContainer("WatchHistory", ApplicationDataCreateDisposition.Always);

    public static IReadOnlyList<WatchProgress> GetWatchHistory()
    {
        // Ignore corrupt or incomplete entries independently so one bad value
        // cannot hide the rest of the shelf. Very short starts are not history.
        var history = new List<WatchProgress>();
        foreach (var value in WatchHistory.Values.Values.OfType<string>())
        {
            try
            {
                var progress = JsonSerializer.Deserialize(value, StremioJsonContext.Default.WatchProgress);
                if (progress?.Item is not null && !string.IsNullOrWhiteSpace(progress.Item.Id) &&
                    !string.IsNullOrWhiteSpace(progress.VideoId) && double.IsFinite(progress.PositionSeconds) &&
                    double.IsFinite(progress.DurationSeconds) && progress.PositionSeconds >= 10 &&
                    progress.DurationSeconds > progress.PositionSeconds && progress.Percent < 95)
                    history.Add(progress);
            }
            catch (JsonException) { }
        }
        return history.OrderByDescending(item => item.UpdatedAt).Take(12).ToList();
    }

    public static WatchProgress? GetWatchProgress(string type, string id, string videoId) =>
        GetWatchHistory().FirstOrDefault(progress => progress.Item.Type == type &&
            progress.Item.Id == id && progress.VideoId == videoId);

    public static void SaveWatchProgress(MetaItem item, string videoId, double position, double duration, bool completed = false)
    {
        // Store only title identity and artwork, never expiring or credential-bearing
        // stream URLs. One bounded value per title avoids LocalSettings' string limit.
        try
        {
            // Key by title rather than episode: a series occupies one shelf card,
            // while VideoId remembers which episode should resume.
            var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes($"{item.Type}/{item.Id}")));
            if (!double.IsFinite(position) || !double.IsFinite(duration)) return;
            // Treat the final 5% as finished to avoid keeping end credits on Home.
            // The explicit flag also handles players that reset position at EOF.
            if (completed || (duration > 0 && position / duration >= 0.95))
            {
                WatchHistory.Values.Remove(key);
                return;
            }
            // Preserve previous progress when opening fails or duration is unknown.
            if (position < 10 || duration <= position) return;
            var snapshot = new MetaItem { Id = item.Id, Type = item.Type, Name = item.Name,
                Poster = item.Poster, ReleaseInfo = item.ReleaseInfo };
            var progress = new WatchProgress(snapshot, videoId, position, duration, DateTimeOffset.UtcNow);
            WatchHistory.Values[key] = JsonSerializer.Serialize(progress, StremioJsonContext.Default.WatchProgress);
            // Prune storage as well as the displayed list to keep history bounded.
            var keep = GetWatchHistory().Select(entry => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes($"{entry.Item.Type}/{entry.Item.Id}")))).ToHashSet();
            foreach (var stale in WatchHistory.Values.Keys.Where(value => !keep.Contains(value)).ToList())
                WatchHistory.Values.Remove(stale);
        }
        catch (Exception exception) { DiagnosticsService.Current.Warn("watch-history", "Could not save progress: " + exception.Message); }
    }

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

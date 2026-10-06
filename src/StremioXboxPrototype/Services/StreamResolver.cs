using StremioXboxPrototype.Models;

namespace StremioXboxPrototype.Services;

public static class StreamResolver
{
    public static StreamResolution Resolve(StreamItem stream)
    {
        // Classification order matters: magnet URLs and HTTP URLs requiring
        // proxy headers must reach the server instead of the native player.
        var torrentUri = string.IsNullOrWhiteSpace(stream.Url) ? stream.ExternalUrl : stream.Url;
        if (!string.IsNullOrWhiteSpace(torrentUri) &&
            Uri.TryCreate(torrentUri, UriKind.Absolute, out var magnetUri) &&
            string.Equals(magnetUri.Scheme, "magnet", StringComparison.OrdinalIgnoreCase))
        {
            return StreamResolution.Service("Streaming service: magnet torrent source");
        }

        if (!string.IsNullOrWhiteSpace(stream.Url) &&
            Uri.TryCreate(stream.Url, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            if (stream.BehaviorHints?.NotWebReady == true || stream.BehaviorHints?.ProxyHeaders is not null)
            {
                return StreamResolution.Unsupported("Proxy or request headers are not supported");
            }

            return StreamResolution.Direct(uri);
        }

        if (!string.IsNullOrWhiteSpace(stream.InfoHash))
        {
            return StreamResolution.Service("Streaming service: torrent source");
        }

        if (!string.IsNullOrWhiteSpace(stream.NzbUrl) ||
            stream.RarUrls.Count > 0 ||
            stream.ZipUrls.Count > 0 ||
            stream.SevenZipUrls.Count > 0 ||
            stream.TgzUrls.Count > 0 ||
            stream.TarUrls.Count > 0)
        {
            return StreamResolution.Unsupported("Archive and Usenet sources are not supported");
        }

        if (!string.IsNullOrWhiteSpace(stream.YouTubeId))
        {
            // The UWP player does not embed web players; callers may offer an
            // explicit handoff for sources that require an external experience.
            return StreamResolution.External("External player: YouTube");
        }

        if (!string.IsNullOrWhiteSpace(stream.ExternalUrl))
        {
            return StreamResolution.External("External web destination");
        }

        return StreamResolution.Unsupported("Unsupported or malformed source");
    }
}

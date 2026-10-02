using StremioXboxPrototype.Models;

namespace StremioXboxPrototype.Services;

public static class StreamResolver
{
    public static StreamResolution Resolve(StreamItem stream)
    {
        if (!string.IsNullOrWhiteSpace(stream.Url) &&
            Uri.TryCreate(stream.Url, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            if (stream.BehaviorHints?.NotWebReady == true || stream.BehaviorHints?.ProxyHeaders is not null)
            {
                return StreamResolution.Service("Streaming service: proxy or headers required");
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
            return StreamResolution.Service("Streaming service: packaged source");
        }

        if (!string.IsNullOrWhiteSpace(stream.YouTubeId))
        {
            return StreamResolution.External("External player: YouTube");
        }

        if (!string.IsNullOrWhiteSpace(stream.ExternalUrl))
        {
            return StreamResolution.External("External web destination");
        }

        return StreamResolution.Unsupported("Unsupported or malformed source");
    }
}

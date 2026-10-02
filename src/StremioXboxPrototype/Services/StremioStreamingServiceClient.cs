using System.Net.Http;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using StremioXboxPrototype.Models;

namespace StremioXboxPrototype.Services;

public sealed class StremioStreamingServiceClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };
    private static readonly ConcurrentDictionary<string, (Uri PlaybackUri, DateTimeOffset Expires)> PreparedTorrentCache = new();

    public async Task TestAsync(Uri serviceUrl, CancellationToken cancellationToken = default)
    {
        using var response = await Http.GetAsync(new Uri(Normalize(serviceUrl), "settings"), cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Stremio Service returned HTTP {(int)response.StatusCode}.");
    }

    public async Task<Uri> ResolveTorrentAsync(Uri serviceUrl, StreamItem stream,
        CancellationToken cancellationToken = default)
    {
        var infoHash = stream.InfoHash?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(infoHash) ||
            (infoHash.Length != 40 && infoHash.Length != 64) ||
            infoHash.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidDataException("The add-on returned an invalid torrent info hash.");

        var root = Normalize(serviceUrl);
        var fileIndex = stream.FileIndex ?? -1;
        var cacheKey = $"{root.AbsoluteUri}|{infoHash}|{fileIndex}";
        if (PreparedTorrentCache.TryGetValue(cacheKey, out var cached) && cached.Expires > DateTimeOffset.UtcNow)
        {
            DiagnosticsService.Current.Info("streaming-service", "Reusing prepared torrent URL");
            return cached.PlaybackUri;
        }

        var sources = new List<string> { $"dht:{infoHash}" };
        foreach (var encodedSource in stream.Sources.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            var source = Uri.UnescapeDataString(encodedSource.Trim());
            sources.Add(source.StartsWith("dht:", StringComparison.OrdinalIgnoreCase) ||
                        source.StartsWith("tracker:", StringComparison.OrdinalIgnoreCase)
                ? source
                : "tracker:" + source);
        }

        var request = new StremioTorrentCreateRequest
        {
            PeerSearch = new StremioPeerSearch { Sources = sources.Distinct(StringComparer.Ordinal).ToList() }
        };
        var json = JsonSerializer.Serialize(request, StremioJsonContext.Default.StremioTorrentCreateRequest);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var timer = Stopwatch.StartNew();
        using var response = await Http.PostAsync(new Uri(root, $"{infoHash}/create"), content, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Stremio Service could not prepare the stream (HTTP {(int)response.StatusCode}).");

        var playbackUri = new Uri(root, $"{infoHash}/{fileIndex}?external=1");
        PreparedTorrentCache[cacheKey] = (playbackUri, DateTimeOffset.UtcNow.AddMinutes(15));
        DiagnosticsService.Current.Info("streaming-service", $"Torrent prepared in {timer.ElapsedMilliseconds} ms");
        return playbackUri;
    }

    private static Uri Normalize(Uri value)
    {
        if (value.Scheme != Uri.UriSchemeHttp && value.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("The Stremio Service URL must use HTTP or HTTPS.", nameof(value));
        return new Uri(value.AbsoluteUri.TrimEnd('/') + "/");
    }
}

using System.Net.Http;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using StremioXboxPrototype.Models;

namespace StremioXboxPrototype.Services;

public sealed class StremioStreamingServiceClient
{
    // Redirects are inspected explicitly when the service returns the prepared
    // playback URL, so automatic redirect following must remain disabled.
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        // Connection checks and torrent preparation have different time limits.
        // Individual operations apply bounded cancellation tokens below.
        Timeout = Timeout.InfiniteTimeSpan
    };
    // Preparing a torrent is expensive. Reuse its playback URL briefly, keyed by
    // server, hash, and selected file, while keeping the cache process-local.
    private static readonly ConcurrentDictionary<string, (Uri PlaybackUri, DateTimeOffset Expires)> PreparedTorrentCache = new();

    public async Task<string> TestAsync(Uri serviceUrl, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));
        using var response = await Http.GetAsync(new Uri(Normalize(serviceUrl), "settings"), timeout.Token);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Stremio Service returned HTTP {(int)response.StatusCode}.");
        using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var settings = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
        var values = settings.RootElement;
        if (values.ValueKind == JsonValueKind.Object && values.TryGetProperty("values", out var nested)) values = nested;
        if (values.ValueKind != JsonValueKind.Object ||
            !values.TryGetProperty("serverVersion", out var version) ||
            version.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(version.GetString()))
            throw new InvalidDataException("This address did not return Stremio server settings. Check the address and port.");
        return version.GetString()!;
    }

    public async Task<Uri> ResolveTorrentAsync(Uri serviceUrl, StreamItem stream,
        CancellationToken cancellationToken = default)
    {
        var infoHash = GetInfoHash(stream);
        if (string.IsNullOrWhiteSpace(infoHash) ||
            (infoHash.Length != 40 && infoHash.Length != 64) ||
            infoHash.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidDataException("The add-on returned an invalid torrent info hash.");

        var root = Normalize(serviceUrl);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        var operationToken = timeout.Token;
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
            AddPeerSource(sources, encodedSource);
        }
        foreach (var tracker in GetMagnetTrackers(GetTorrentUri(stream))) AddPeerSource(sources, tracker);

        var request = new StremioTorrentCreateRequest
        {
            PeerSearch = new StremioPeerSearch { Sources = sources.Distinct(StringComparer.Ordinal).ToList() }
        };
        var json = JsonSerializer.Serialize(request, StremioJsonContext.Default.StremioTorrentCreateRequest);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var timer = Stopwatch.StartNew();
        HttpResponseMessage response;
        try
        {
            response = await Http.PostAsync(new Uri(root, $"{infoHash}/create"), content, operationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("The streaming service could not find torrent metadata within two minutes. Try another source with more peers.");
        }
        using (response)
        {
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Stremio Service could not prepare the stream (HTTP {(int)response.StatusCode}).");
        }

        Uri playbackUri;
        try
        {
            playbackUri = await GetPlaybackUriAsync(root, infoHash, fileIndex, operationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("The streaming service prepared the torrent but did not return a playback URL in time. Try another source.");
        }
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

    private static string? GetInfoHash(StreamItem stream)
    {
        var value = stream.InfoHash?.Trim();
        if (string.IsNullOrWhiteSpace(value) && Uri.TryCreate(GetTorrentUri(stream), UriKind.Absolute, out var magnetUri) &&
            string.Equals(magnetUri.Scheme, "magnet", StringComparison.OrdinalIgnoreCase))
        {
            var hashValue = GetMagnetValues(magnetUri).FirstOrDefault(pair =>
                pair.Key.Equals("xt", StringComparison.OrdinalIgnoreCase) &&
                pair.Value.StartsWith("urn:btih:", StringComparison.OrdinalIgnoreCase)).Value;
            if (!string.IsNullOrWhiteSpace(hashValue)) value = hashValue["urn:btih:".Length..];
        }
        return value?.ToLowerInvariant();
    }

    private static IEnumerable<string> GetMagnetTrackers(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var magnetUri) ||
            !string.Equals(magnetUri.Scheme, "magnet", StringComparison.OrdinalIgnoreCase)) return Array.Empty<string>();
        return GetMagnetValues(magnetUri).Where(pair => pair.Key.Equals("tr", StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair.Value);
    }

    private static string? GetTorrentUri(StreamItem stream) =>
        string.IsNullOrWhiteSpace(stream.Url) ? stream.ExternalUrl : stream.Url;

    private static IEnumerable<KeyValuePair<string, string>> GetMagnetValues(Uri magnetUri) =>
        magnetUri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Select(part =>
        {
            var separator = part.IndexOf('=');
            var key = separator < 0 ? part : part[..separator];
            var value = separator < 0 ? "" : part[(separator + 1)..];
            return new KeyValuePair<string, string>(Uri.UnescapeDataString(key), Uri.UnescapeDataString(value));
        });

    private static void AddPeerSource(ICollection<string> sources, string value)
    {
        var source = Uri.UnescapeDataString(value.Trim());
        if (string.IsNullOrWhiteSpace(source)) return;
        if (source.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase)) return;
        sources.Add(source.StartsWith("dht:", StringComparison.OrdinalIgnoreCase) ||
                    source.StartsWith("tracker:", StringComparison.OrdinalIgnoreCase)
            ? source
            : "tracker:" + source);
    }

    private static async Task<Uri> GetPlaybackUriAsync(Uri root, string infoHash, int fileIndex,
        CancellationToken cancellationToken)
    {
        // external=1 asks Stremio Service for a redirect to the URL intended for
        // a separate media player. Some versions stream directly instead.
        var externalUri = new Uri(root, $"{infoHash}/{fileIndex}?external=1");
        using var request = new HttpRequestMessage(HttpMethod.Get, externalUri);
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is not null)
        {
            var location = response.Headers.Location;
            if (!location.IsAbsoluteUri) return new Uri(root, location);

            // Hosted Stremio servers commonly advertise their container/LAN
            // address in Location. Keep the returned path, but route playback
            // through the public server address that the Xbox can reach.
            if (!string.Equals(location.Host, root.Host, StringComparison.OrdinalIgnoreCase) ||
                location.Port != root.Port || location.Scheme != root.Scheme)
            {
                DiagnosticsService.Current.Info("streaming-service",
                    $"Rewrote private playback redirect from {location.Host} to {root.Host}");
                return new Uri(root, location.PathAndQuery.TrimStart('/'));
            }
            return location;
        }
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Stremio Service could not open the prepared torrent (HTTP {(int)response.StatusCode}).");
        return new Uri(root, $"{infoHash}/{fileIndex}");
    }
}

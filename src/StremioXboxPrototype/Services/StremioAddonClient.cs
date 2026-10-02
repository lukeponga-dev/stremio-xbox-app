using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using StremioXboxPrototype.Models;

namespace StremioXboxPrototype.Services;

public sealed class StremioAddonClient
{
    public static readonly Uri CinemetaManifest = new("https://v3-cinemeta.strem.io/manifest.json");
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };
    private static readonly ConcurrentDictionary<string, (AddonManifest Manifest, DateTimeOffset Expires)> ManifestCache = new();

    public async Task<AddonManifest> GetManifestAsync(Uri manifestUri, CancellationToken cancellationToken = default)
    {
        if (ManifestCache.TryGetValue(manifestUri.AbsoluteUri, out var cached) && cached.Expires > DateTimeOffset.UtcNow)
        {
            return cached.Manifest;
        }

        var manifest = await GetAsync(manifestUri, StremioJsonContext.Default.AddonManifest, "manifest", cancellationToken);
        ValidateManifest(manifest, manifestUri);
        ManifestCache[manifestUri.AbsoluteUri] = (manifest, DateTimeOffset.UtcNow.AddMinutes(10));
        return manifest;
    }

    public async Task<IReadOnlyList<MetaItem>> GetCatalogAsync(
        string type,
        string catalogId = "top",
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        var manifest = await GetManifestAsync(CinemetaManifest, cancellationToken);
        if (!SupportsResource(manifest, "catalog", type, catalogId))
            throw new NotSupportedException($"{manifest.Name} does not declare catalog support for {type}/{catalogId}.");

        var catalog = manifest.Catalogs.FirstOrDefault(value => value.Type == type && value.Id == catalogId);
        if (!string.IsNullOrWhiteSpace(search) && catalog?.Extra.Any(value => value.Name == "search") != true)
            throw new NotSupportedException($"{manifest.Name} does not declare search support for {type}/{catalogId}.");

        var extra = string.IsNullOrWhiteSpace(search)
            ? ""
            : "/search=" + Uri.EscapeDataString(search.Trim());
        var uri = BuildResourceUri(CinemetaManifest, $"catalog/{type}/{catalogId}{extra}.json");
        var response = await GetAsync(uri, StremioJsonContext.Default.CatalogResponse, "catalog", cancellationToken);
        return response.Metas;
    }

    public async Task<MetaItem?> GetMetaAsync(string type, string id, CancellationToken cancellationToken = default)
    {
        var manifest = await GetManifestAsync(CinemetaManifest, cancellationToken);
        if (!SupportsResource(manifest, "meta", type, id))
            throw new NotSupportedException($"{manifest.Name} does not declare metadata support for {type}/{id}.");

        var uri = BuildResourceUri(CinemetaManifest, $"meta/{type}/{Uri.EscapeDataString(id)}.json");
        return (await GetAsync(uri, StremioJsonContext.Default.MetaResponse, "meta", cancellationToken)).Meta;
    }

    public async Task<IReadOnlyList<StreamItem>> GetStreamsAsync(
        AddonEndpoint addon,
        string type,
        string videoId,
        CancellationToken cancellationToken = default)
    {
        var manifest = await GetManifestAsync(addon.ManifestUri, cancellationToken);
        if (!SupportsResource(manifest, "stream", type, videoId))
        {
            DiagnosticsService.Current.Info("streams", $"Skipped {manifest.Name}: unsupported {type}/{videoId}");
            return Array.Empty<StreamItem>();
        }

        var uri = BuildResourceUri(addon.ManifestUri,
            $"stream/{type}/{Uri.EscapeDataString(videoId)}.json");
        var response = await GetAsync(uri, StremioJsonContext.Default.StreamResponse, "streams", cancellationToken);
        foreach (var stream in response.Streams)
        {
            stream.Provider = manifest.Name;
            stream.Resolution = StreamResolver.Resolve(stream);
        }
        return response.Streams;
    }

    public static bool SupportsResource(AddonManifest manifest, string resource, string type, string id)
    {
        foreach (var descriptor in manifest.Resources)
        {
            string? name = null;
            IReadOnlyList<string> types = manifest.Types;
            IReadOnlyList<string> prefixes = manifest.IdPrefixes;

            if (descriptor.ValueKind == JsonValueKind.String)
            {
                name = descriptor.GetString();
            }
            else if (descriptor.ValueKind == JsonValueKind.Object)
            {
                if (descriptor.TryGetProperty("name", out var nameValue)) name = nameValue.GetString();
                if (descriptor.TryGetProperty("types", out var typeValues) && typeValues.ValueKind == JsonValueKind.Array)
                    types = typeValues.EnumerateArray().Select(value => value.GetString() ?? "").Where(value => value.Length > 0).ToList();
                if (descriptor.TryGetProperty("idPrefixes", out var prefixValues) && prefixValues.ValueKind == JsonValueKind.Array)
                    prefixes = prefixValues.EnumerateArray().Select(value => value.GetString() ?? "").Where(value => value.Length > 0).ToList();
            }

            if (!string.Equals(name, resource, StringComparison.Ordinal)) continue;
            if (types.Count > 0 && !types.Contains(type, StringComparer.Ordinal)) continue;
            if (resource != "catalog" && prefixes.Count > 0 &&
                !prefixes.Any(prefix => id.StartsWith(prefix, StringComparison.Ordinal))) continue;
            if (resource == "catalog" && !manifest.Catalogs.Any(value => value.Type == type && value.Id == id)) continue;
            return true;
        }

        return false;
    }

    private static void ValidateManifest(AddonManifest manifest, Uri uri)
    {
        manifest.Resources ??= new();
        manifest.Types ??= new();
        manifest.IdPrefixes ??= new();
        manifest.Catalogs ??= new();

        if (string.IsNullOrWhiteSpace(manifest.Id) ||
            string.IsNullOrWhiteSpace(manifest.Version) ||
            string.IsNullOrWhiteSpace(manifest.Name) ||
            manifest.Resources.Count == 0 ||
            manifest.Types.Count == 0)
        {
            throw new InvalidDataException($"{uri.Host} did not return a valid Stremio add-on manifest.");
        }
    }

    private static Uri BuildResourceUri(Uri manifestUri, string path)
    {
        var manifest = manifestUri.AbsoluteUri;
        var root = manifest.EndsWith("manifest.json", StringComparison.OrdinalIgnoreCase)
            ? manifest[..^"manifest.json".Length]
            : manifest.TrimEnd('/') + "/";
        return new Uri(root + path);
    }

    private static async Task<T> GetAsync<T>(
        Uri uri,
        JsonTypeInfo<T> typeInfo,
        string area,
        CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        DiagnosticsService.Current.Info(area, $"GET {uri.Host}{uri.AbsolutePath}");
        try
        {
            using var response = await Http.GetAsync(uri, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var value = await JsonSerializer.DeserializeAsync(stream, typeInfo, cancellationToken);
            DiagnosticsService.Current.Info(area, $"{(int)response.StatusCode} in {timer.ElapsedMilliseconds} ms");
            return value ?? throw new InvalidDataException("The add-on returned an empty response.");
        }
        catch (Exception exception)
        {
            DiagnosticsService.Current.Error(area, $"Failed after {timer.ElapsedMilliseconds} ms: {exception.Message}");
            throw;
        }
    }
}

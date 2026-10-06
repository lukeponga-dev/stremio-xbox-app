using System.Text.Json;
using System.Text.Json.Serialization;

namespace StremioXboxPrototype.Models;

public sealed class StremioLoginRequest
{
    [JsonPropertyName("authKey")]
    public string? AuthKey { get; set; }
    [JsonPropertyName("email")]
    public string Email { get; set; } = "";
    [JsonPropertyName("password")]
    public string Password { get; set; } = "";
    [JsonPropertyName("facebook")]
    public bool Facebook { get; set; }
}

public class StremioAuthenticatedRequest
{
    [JsonPropertyName("authKey")]
    public string AuthKey { get; set; } = "";
}

public sealed class StremioAddonCollectionRequest : StremioAuthenticatedRequest
{
    [JsonPropertyName("update")]
    public bool Update { get; set; } = true;
    [JsonPropertyName("addFromURL")]
    public List<string> AddFromUrl { get; set; } = new();
}

public sealed class StremioLoginEnvelope
{
    [JsonPropertyName("result")]
    public StremioLoginResult? Result { get; set; }
    [JsonPropertyName("error")]
    public JsonElement? Error { get; set; }
}

public sealed class StremioUserEnvelope
{
    [JsonPropertyName("result")]
    public StremioUser? Result { get; set; }
    [JsonPropertyName("error")]
    public JsonElement? Error { get; set; }
}

public sealed class StremioApiEnvelope
{
    [JsonPropertyName("result")]
    public JsonElement? Result { get; set; }
    [JsonPropertyName("error")]
    public JsonElement? Error { get; set; }
}

public sealed class StremioLoginResult
{
    [JsonPropertyName("authKey")]
    public string AuthKey { get; set; } = "";
    [JsonPropertyName("user")]
    public StremioUser? User { get; set; }
}

public sealed class StremioFacebookCredentialsEnvelope
{
    [JsonPropertyName("user")]
    public StremioFacebookCredentials? User { get; set; }
}

public sealed class StremioFacebookCredentials
{
    [JsonPropertyName("email")]
    public string Email { get; set; } = "";
    [JsonPropertyName("fbLoginToken")]
    public string LoginToken { get; set; } = "";
}

public sealed class StremioUser
{
    [JsonPropertyName("_id")]
    public string Id { get; set; } = "";
    [JsonPropertyName("email")]
    public string Email { get; set; } = "";
    [JsonPropertyName("avatar")]
    public string? Avatar { get; set; }
}

public sealed record StremioAccountSession(string Email, string AuthKey);

public sealed record AccountProfileCache(string Email, string Id, string? Avatar, int AddonCount);

public sealed record StremioFacebookLoginAttempt(string State, Uri LoginUri);

public sealed class StremioAddonCollectionEnvelope
{
    [JsonPropertyName("result")]
    public StremioAddonCollectionResult? Result { get; set; }
    [JsonPropertyName("error")]
    public JsonElement? Error { get; set; }
}

public sealed class StremioAddonCollectionResult
{
    [JsonPropertyName("addons")]
    public List<StremioAddonDescriptor> Addons { get; set; } = new();
    [JsonPropertyName("lastModified")]
    public string? LastModified { get; set; }
}

public sealed class StremioAddonDescriptor
{
    [JsonPropertyName("manifest")]
    public AddonManifest? Manifest { get; set; }
    [JsonPropertyName("transportUrl")]
    public string TransportUrl { get; set; } = "";
    [JsonPropertyName("flags")]
    public JsonElement? Flags { get; set; }
}

public sealed class AddonManifest
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    // The official protocol permits each resource to be either a string or
    // an object with its own types and idPrefixes. JsonElement preserves both.
    [JsonPropertyName("resources")]
    public List<JsonElement> Resources { get; set; } = new();

    [JsonPropertyName("types")]
    public List<string> Types { get; set; } = new();

    [JsonPropertyName("idPrefixes")]
    public List<string> IdPrefixes { get; set; } = new();

    [JsonPropertyName("catalogs")]
    public List<AddonCatalog> Catalogs { get; set; } = new();

    [JsonPropertyName("behaviorHints")]
    public AddonManifestBehaviorHints? BehaviorHints { get; set; }
}

public sealed class AddonCatalog
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("extra")]
    public List<AddonCatalogExtra> Extra { get; set; } = new();
}

public sealed class AddonCatalogExtra
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("isRequired")]
    public bool IsRequired { get; set; }

    [JsonPropertyName("options")]
    public List<string> Options { get; set; } = new();
}

public sealed class AddonManifestBehaviorHints
{
    [JsonPropertyName("configurable")]
    public bool Configurable { get; set; }

    [JsonPropertyName("configurationRequired")]
    public bool ConfigurationRequired { get; set; }

    [JsonPropertyName("p2p")]
    public bool P2P { get; set; }

    [JsonPropertyName("adult")]
    public bool Adult { get; set; }
}

public sealed class CatalogResponse
{
    [JsonPropertyName("metas")]
    public List<MetaItem> Metas { get; set; } = new();
}

public sealed class MetaResponse
{
    [JsonPropertyName("meta")]
    public MetaItem? Meta { get; set; }
}

public sealed class MetaItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "movie";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "Untitled";

    [JsonPropertyName("poster")]
    public string? Poster { get; set; }

    [JsonPropertyName("background")]
    public string? Background { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("releaseInfo")]
    public string? ReleaseInfo { get; set; }

    [JsonPropertyName("imdbRating")]
    public string? ImdbRating { get; set; }

    [JsonPropertyName("runtime")]
    public string? Runtime { get; set; }

    [JsonPropertyName("videos")]
    public List<VideoItem> Videos { get; set; } = new();

    [JsonIgnore]
    public string Subtitle => string.IsNullOrWhiteSpace(ReleaseInfo) ? Type : $"{Type} · {ReleaseInfo}";

    [JsonIgnore]
    public string CardMetadata => string.Join(" · ", new[]
    {
        ReleaseInfo,
        string.IsNullOrWhiteSpace(ImdbRating) ? null : $"★ {ImdbRating}",
        Runtime
    }.Where(value => !string.IsNullOrWhiteSpace(value))) is { Length: > 0 } details
        ? details : "Details unavailable";
}

public sealed class VideoItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("season")]
    public int? Season { get; set; }

    [JsonPropertyName("episode")]
    public int? Episode { get; set; }

    [JsonIgnore]
    public string DisplayTitle => Title ?? (Season.HasValue ? $"S{Season:00} E{Episode:00}" : Id);
}

public sealed class StreamResponse
{
    [JsonPropertyName("streams")]
    public List<StreamItem> Streams { get; set; } = new();
}

public sealed class StreamItem
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("ytId")]
    public string? YouTubeId { get; set; }

    [JsonPropertyName("infoHash")]
    public string? InfoHash { get; set; }

    [JsonPropertyName("fileIdx")]
    public int? FileIndex { get; set; }

    [JsonPropertyName("fileMustInclude")]
    public string? FileMustInclude { get; set; }

    [JsonPropertyName("sources")]
    public List<string> Sources { get; set; } = new();

    [JsonPropertyName("nzbUrl")]
    public string? NzbUrl { get; set; }

    [JsonPropertyName("rarUrls")]
    public List<StreamSource> RarUrls { get; set; } = new();

    [JsonPropertyName("zipUrls")]
    public List<StreamSource> ZipUrls { get; set; } = new();

    [JsonPropertyName("7zipUrls")]
    public List<StreamSource> SevenZipUrls { get; set; } = new();

    [JsonPropertyName("tgzUrls")]
    public List<StreamSource> TgzUrls { get; set; } = new();

    [JsonPropertyName("tarUrls")]
    public List<StreamSource> TarUrls { get; set; } = new();

    [JsonPropertyName("externalUrl")]
    public string? ExternalUrl { get; set; }

    [JsonPropertyName("behaviorHints")]
    public StreamBehaviorHints? BehaviorHints { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalSources { get; set; }

    [JsonIgnore]
    public string Provider { get; set; } = "Unknown add-on";

    [JsonIgnore]
    public StreamResolution Resolution { get; set; } = StreamResolution.Unsupported("Not resolved");

    [JsonIgnore]
    public string DisplayName => string.Join(" · ", new[] { Provider, Name, Description ?? Title }
        .Where(value => !string.IsNullOrWhiteSpace(value)));

    [JsonIgnore]
    public string ResolutionLabel => Resolution.Label;
}

public sealed class StreamBehaviorHints
{
    [JsonPropertyName("notWebReady")]
    public bool NotWebReady { get; set; }

    [JsonPropertyName("proxyHeaders")]
    public JsonElement? ProxyHeaders { get; set; }

    [JsonPropertyName("countryWhitelist")]
    public List<string> CountryWhitelist { get; set; } = new();

    [JsonPropertyName("bingeGroup")]
    public string? BingeGroup { get; set; }

    [JsonPropertyName("videoHash")]
    public string? VideoHash { get; set; }

    [JsonPropertyName("videoSize")]
    public long? VideoSize { get; set; }

    [JsonPropertyName("filename")]
    public string? Filename { get; set; }
}

public sealed class StreamSource
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("bytes")]
    public long? Bytes { get; set; }
}

public sealed class StremioTorrentCreateRequest
{
    [JsonPropertyName("peerSearch")]
    public StremioPeerSearch? PeerSearch { get; set; }
}

public sealed class StremioPeerSearch
{
    [JsonPropertyName("min")]
    public int Min { get; set; } = 40;

    [JsonPropertyName("max")]
    public int Max { get; set; } = 200;

    [JsonPropertyName("sources")]
    public List<string> Sources { get; set; } = new();
}

public enum StreamResolutionKind
{
    NativeDirect,
    RequiresStreamingService,
    External,
    Unsupported
}

public sealed record StreamResolution(StreamResolutionKind Kind, string Label, Uri? PlaybackUri)
{
    public static StreamResolution Direct(Uri uri) => new(StreamResolutionKind.NativeDirect, "Native direct", uri);
    public static StreamResolution Service(string reason) => new(StreamResolutionKind.RequiresStreamingService, reason, null);
    public static StreamResolution External(string reason) => new(StreamResolutionKind.External, reason, null);
    public static StreamResolution Unsupported(string reason) => new(StreamResolutionKind.Unsupported, reason, null);
}

public sealed record AddonEndpoint(string Name, Uri ManifestUri);

public sealed record PlaybackRequest(Uri Uri, string Title, string Source);
public sealed record StreamPlaybackRequest(StreamItem Stream, Uri ServiceUri, string Title);

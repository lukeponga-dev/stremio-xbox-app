using System.Text.Json.Serialization;

namespace StremioXboxPrototype.Models;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(StremioLoginRequest))]
[JsonSerializable(typeof(StremioAuthenticatedRequest))]
[JsonSerializable(typeof(StremioAddonCollectionRequest))]
[JsonSerializable(typeof(StremioLoginEnvelope))]
[JsonSerializable(typeof(StremioFacebookCredentialsEnvelope))]
[JsonSerializable(typeof(StremioUserEnvelope))]
[JsonSerializable(typeof(StremioApiEnvelope))]
[JsonSerializable(typeof(StremioAddonCollectionEnvelope))]
[JsonSerializable(typeof(AddonManifest))]
[JsonSerializable(typeof(CatalogResponse))]
[JsonSerializable(typeof(MetaResponse))]
[JsonSerializable(typeof(StreamResponse))]
[JsonSerializable(typeof(StremioTorrentCreateRequest))]
[JsonSerializable(typeof(List<MetaItem>))]
[JsonSerializable(typeof(WatchProgress))]
internal sealed partial class StremioJsonContext : JsonSerializerContext;

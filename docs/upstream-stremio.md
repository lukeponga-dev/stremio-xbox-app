# Upstream Stremio reuse boundary

Checked against the upstream projects on 2 October 2026.

## Sources of truth

| Upstream project | Used for | Integration in this prototype |
| --- | --- | --- |
| [Stremio/stremio-core](https://github.com/Stremio/stremio-core) | Architecture, shared type vocabulary, add-on transport behavior, model boundaries | The XAML UI, protocol adapter, and playback adapter remain separate. Required core types are represented in `Models/StremioModels.cs`. |
| [Stremio/stremio-addon-sdk protocol](https://github.com/Stremio/stremio-addon-sdk/blob/master/docs/protocol.md) | Resource URL format and response envelopes | Requests use `/{resource}/{type}/{id}.json` and extra arguments use the documented path form. |
| [Manifest specification](https://github.com/Stremio/stremio-addon-sdk/blob/master/docs/api/responses/manifest.md) | Resource/type/ID-prefix routing and catalog extras | Every configured add-on is fetched and validated. Requests are sent only when its manifest declares support. |
| [Stream specification](https://github.com/Stremio/stremio-addon-sdk/blob/master/docs/api/responses/stream.md) | Direct URLs, torrents, archive/NZB sources, external URLs, proxy hints, subtitles, and file metadata | Direct HTTP(S) URLs can reach `MediaPlayerElement`; all service-backed or external sources are classified explicitly. |
| [Stremio/stremio-web](https://github.com/Stremio/stremio-web) | Thin UI and platform playback-adapter precedent | The UWP shell renders state and delegates playback to a native adapter rather than mixing network and player logic into views. |
| [Stremio/stremio-api-client](https://github.com/Stremio/stremio-api-client) | Account request transport, user refresh, and add-on collection sync | `StremioAccountClient` sends JSON POST requests to `https://api.strem.io/api/{method}`, includes `authKey` in authenticated bodies, and implements `login`, `getUser`, `logout`, and `addonCollectionGet`. |
| [Stremio/stremio-web](https://github.com/Stremio/stremio-web) | Facebook browser sign-in handoff | The account screen follows the upstream `login-fb/{state}` handoff, polls `login-fb-get-acc/{state}`, and submits the returned one-time token through the Stremio login API with `facebook: true`. |

## Account connection

The Account screen sends the entered email and password directly to Stremio's HTTPS API. The password is discarded after the login response. Only the returned `authKey` is persisted, using Windows Credential Locker; it is never written to diagnostics. Account add-on descriptors are read from `addonCollectionGet`, and their `transportUrl` values are used unchanged so configured add-ons retain their user-specific paths.

The account API is used for identity and synchronized configuration. Add-on resource requests still go directly to each add-on's declared transport URL, while playback still goes through the native media adapter or a separately configured streaming service.

## Why the repositories are not copied into the app

`stremio-addon-sdk` is a Node.js library for implementing add-on servers, not a client dependency. `stremio-core` is a Rust crate and its maintained first-party bridge targets WebAssembly; upstream does not publish a supported UWP/.NET binding. Copying either repository into the package would not make it callable by the UWP app and would add substantial unused code.

The current bounded implementation therefore uses the official wire contracts and architecture while keeping a narrow replacement seam at `StremioAddonClient`. It now:

1. Loads and caches each add-on manifest.
2. Validates required manifest fields.
3. Supports both string and object resource declarations.
4. Applies manifest-level and resource-level `types` and `idPrefixes`.
5. Verifies catalog declarations and search extras before issuing a request.
6. Models all documented stream source families and behavior hints needed for feasibility classification.

## Next reuse spike

Before production, build a small Rust `staticlib` wrapper around `stremio-core` with a JSON/C ABI and test it in an Xbox UWP package. The gate is not whether the Rust crate builds for desktop Windows; it is whether the library and its required environment implementation pass Xbox AppContainer deployment, suspension, networking, memory, and Store certification tests. Until that gate passes, the C# adapter remains the working prototype path.

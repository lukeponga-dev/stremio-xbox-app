# Architecture

OpenStream TV is a C# application built with XAML on the modern SDK-style UWP project system. The current solution contains one application project; the test harnesses are standalone .NET console projects under `tests/`.

## Runtime composition

The app is organized by responsibility rather than by separate solution projects:

| Area | Location | Responsibility |
|---|---|---|
| Application startup and shared resources | `App.xaml`, `App.xaml.cs` | Application lifecycle, shared styles and resources |
| Main navigation and primary screens | `MainPage.xaml`, `MainPage.xaml.cs` | Home, Discover, Library, Add-ons, account, server, settings and diagnostics UI |
| Movie/series details | `DetailsPage.xaml`, `DetailsPage.xaml.cs` | Metadata presentation, episode selection and stream selection |
| Playback | `PlayerPage.xaml`, `PlayerPage.xaml.cs` | Native media playback UI and playback lifecycle |
| Reusable controls | `Controls/` | Reusable XAML controls such as `PosterCard` |
| Data contracts | `Models/` | Stremio API/add-on models and JSON source-generation context |
| Application services | `Services/` | Account, add-on, streaming-service, settings, diagnostics, layout and stream helpers |

## Main service boundaries

- **`StremioAddonClient`** requests add-on manifests, catalogs, metadata and streams using the Stremio add-on HTTP contract. It is the app's boundary to configured add-on endpoints and the built-in Cinemeta manifest.
- **`StremioAccountClient`** handles Stremio account operations, including session-based account requests and add-on collection retrieval.
- **`StremioStreamingServiceClient`** communicates with a separately hosted Stremio streaming service. This is distinct from add-on manifest/catalog/stream discovery.
- **`PrototypeSettings`** owns local persisted preferences and collections, including configured stream add-ons, playback-server URL, library and watch history.
- **`DiagnosticsService`** provides bounded diagnostic events for the UI and troubleshooting.
- **`TvLayout`** centralizes responsive layout calculations used by TV-oriented screens.

The UI currently coordinates these services from the page code-behind. The solution does not yet split the application into separate Core, Features, Infrastructure and Playback projects.

## Typical content flow

1. `MainPage` requests catalogs or search results through `StremioAddonClient`.
2. A selected `MetaItem` is passed to `DetailsPage`.
3. The details screen requests available streams from configured add-ons.
4. A selected stream is resolved according to its supported source type and playback path.
5. A playable request is passed to `PlayerPage`, which owns the native playback experience.

The playback server is an optional, separately configured service for supported server-dependent sources. It is not the same thing as an add-on provider.

## Navigation

The main page switches visibility between its named panels through `ShowPanel`. Movie and series details and the player are navigated as separate XAML pages through the frame. The main page also manages controller focus transitions when switching panels.

The visible main areas include Home, Discover, Library and Add-ons. Account, Streaming Service, Settings, Playback Lab and Diagnostics are also represented in the main page UI; some are grouped under developer/advanced navigation.

## Platform and serialization constraints

The application targets `net10.0-windows10.0.26100.0`, uses `UseUwp=true`, and enables NativeAOT publishing. JSON models use `System.Text.Json` source-generation support in `Models/StremioJsonContext.cs`. When changing models or serialization behavior, check the source-generated context and NativeAOT compatibility.

The project has x86, x64 and arm64 platform/RID declarations, while the checked-in Store packaging workflow is configured for x64. Confirm the intended deployment target before changing platform settings.

## Upstream contract references

- [Stremio add-on SDK](https://github.com/Stremio/stremio-addon-sdk)
- [Stremio API client](https://github.com/Stremio/stremio-api-client)
- [Stremio Core](https://github.com/Stremio/stremio-core)
- [Upstream integration notes](upstream-stremio.md)

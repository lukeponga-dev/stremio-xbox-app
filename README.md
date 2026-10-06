# Stremio Xbox feasibility prototype

This repository contains an x64 UWP/XAML prototype for testing a controller-first Stremio experience on Xbox One and Xbox Series X|S. It is an engineering spike, not a production Stremio client.

## Stremio upstream projects

The integration layer follows Stremio's maintained open-source contracts:

- [`Stremio/stremio-core`](https://github.com/Stremio/stremio-core) is the architectural and type-system source of truth.
- [`Stremio/stremio-addon-sdk`](https://github.com/Stremio/stremio-addon-sdk) supplies the HTTP add-on protocol and manifest/stream specifications.
- [`Stremio/stremio-api-client`](https://github.com/Stremio/stremio-api-client) supplies the official account API request pattern, login flow, and add-on collection synchronization contract.
- [`Stremio/stremio-web`](https://github.com/Stremio/stremio-web) is the reference thin-client composition: UI → core → API/add-ons, with playback kept behind a platform adapter.

The Rust core is not binary-linked yet because upstream has no supported UWP/.NET binding. This prototype ports the required manifest, catalog, metadata, and stream contracts into NativeAOT-compatible C# and keeps them isolated behind `StremioAddonClient`. See [docs/upstream-stremio.md](docs/upstream-stremio.md) for the reuse boundary and the path to a Rust/C bridge spike.

## Implemented

- Public movie and series browsing through the official Cinemeta add-on.
- Search through the Stremio add-on protocol.
- Metadata details and episode selection.
- User-configurable HTTPS stream add-on manifests with validation, manifest-declared routing, caching, and concurrent failure-isolated requests.
- Stremio email/password and Facebook sign-in through Stremio's official account flows, secure session-key persistence in Windows Credential Locker, startup/session validation through `getUser`, logout, and account add-on synchronization through `addonCollectionGet`.
- Stream classification into native-direct, streaming-service-dependent, external, and unsupported sources, with optional external Stremio Service resolution for torrent-backed streams.
- Native direct playback with `MediaPlayerElement` and platform transport controls.
- A direct-URL playback lab with a known public test asset.
- Controller-oriented XY focus, visible platform focus, Back navigation, and focus restoration.
- Full-screen TV layout with a compact Search button, dedicated Discover search, controller keyboard, and optional Windows voice recognition.
- Rounded 2:3 poster cards, titles underneath, a bright focus border and 6% zoom, and year/rating/runtime metadata on focus when available. Settings can disable focus animation.
- Home/Discover/Library/Add-ons navigation, with Playback Lab, Server, Diagnostics, and Settings grouped under Developer / Advanced. Catalog counts and technical server information stay in those tools.
- TV-safe margins, scrollable navigation/content, and shelves sized to show roughly 20% of the next card. Home has loading skeletons, empty/error recovery, and an offline playback-server reconnect notice.
- A small local favorites library.
- Bounded diagnostics for requests, playback startup, buffering, failures, lifecycle, and memory usage.
- Internet and private-network capabilities for later external-service testing.

## Deliberately not implemented

- Device-code/QR sign-in. Stremio Core exposes a separate link-service contract, but this prototype currently uses the established account API email/password flow and treats link-service UX as a separate follow-up.
- Torrent, NZB, archive processing, or transcoding on the console.
- A bundled third-party stream provider.
- Proxy/header rewriting, YouTube embedding, or external web playback.
- Store-ready identity, artwork, signing, age rating, or policy approval.

These omissions preserve the proposal's feasibility boundary.

## Build

Prerequisites:

- Visual Studio 2026 with Universal Windows Platform development tools.
- Windows SDK 10.0.26100 or newer.
- .NET 10 SDK.
- Visual Studio's Desktop development with C++ workload for Release/NativeAOT packaging.

From a Developer PowerShell prompt:

```powershell
msbuild .\StremioXboxPrototype.sln /restore /p:Platform=x64 /p:Configuration=Debug
```

## Build a Microsoft Store update

Visual Studio may show the generic .NET Publish window for this SDK-style UWP
project. Azure, ClickOnce, Docker, and Folder profiles do not create a Store
package. Use the checked-in packaging script instead:

```powershell
.\tools\Build-StorePackage.ps1
```

Open and build the solution once in Visual Studio before the first script run so
NuGet dependencies are restored by the UWP-aware project system.

Before running it, increase the four-part `Identity Version` in
`src\StremioXboxPrototype\Package.appxmanifest`. The version must be higher than
the package already in Partner Center, the first number must be at least 1, and
the fourth number must remain 0. The script builds Release/x64, creates a unique
folder under `src\StremioXboxPrototype\AppPackages`, and prints the `.msixupload`
or `.msix` file to upload in Partner Center.

The project is x64-only because current Xbox UWP development and submission no longer supports x86.

## Run on Windows

Open `StremioXboxPrototype.sln`, select `x64` and `Local Machine`, then run without the debugger for representative memory measurements. Debugger-attached runs do not enforce Xbox's normal app memory limit.

The solution includes separate `watchstream (Local Machine)` and
`watchstream (Xbox)` launch profiles. If local deployment reports a signing
certificate error, create and trust a current-user development certificate with:

```powershell
.\tools\Install-DevelopmentCertificate.ps1
```

The script preserves an existing PFX under `SigningTemp`, creates a certificate
whose subject matches the package publisher, and stores its generated password
only in the ignored `.csproj.user` file.

Hot Reload is disabled in the checked-in launch profile. UWP AppContainer launches can deny Visual Studio the process access requested by its Hot Reload session, while normal managed debugging and breakpoints continue to work. If Visual Studio had the solution open before this setting was added, stop debugging and reload the project once.

## Deploy to Xbox Dev Mode

1. Activate Dev Mode on the console and enable remote access in Dev Home.
2. Put the development PC and console on the same network.
3. Open the solution in Visual Studio.
4. Select `x64`, then choose `Remote Machine` as the target.
5. Enter the console IP shown by Dev Home and use Universal authentication when prompted.
6. Deploy and run once from Visual Studio.
7. Repeat measurements from a non-debug deployment.

The remote-debug address must be the **Xbox console IP shown in Dev Home**. It is
separate from the Stremio streaming-server address configured inside the app. The
launch profile currently targets `192.168.1.103`; update it if DHCP changes the
console address. If deployment reports `DEP6957` with `0x8007274D`, reopen the
Remote Connections dialog, select the Xbox again, and confirm Dev Home still has
remote access enabled before requesting a fresh pairing PIN.

For a Device Portal package, use Visual Studio's **Publish → Create App Packages** flow and create a sideload package signed by a certificate trusted on the console. The checked-in publisher identity and template artwork are placeholders.

A Debug MSIX can be produced without NativeAOT. Release packaging enables NativeAOT and therefore requires the C++ linker workload.

## Xbox release gate

The checked-in package is configured for Xbox Dev Mode deployment: it is x64, controller-first, has internet/private-network capabilities, uses the full display with internal content spacing, and exposes platform transport controls. The player also supports **B** to return and **X** to mute or unmute audio.

Before distribution beyond Dev Mode, replace the placeholder identity, certificates, and artwork; complete age ratings and Store policy review; and pass every item in [docs/validation.md](docs/validation.md) on the supported Xbox hardware tiers. The app must not be described as Store-ready until those external release requirements are complete.

## Exercise the prototype

The Home and Discover screens work without account credentials. Playback can be tested in two ways:

1. Open **Playback lab** and play the prefilled Big Buck Bunny URL, or provide another direct HTTP(S) media URL.
2. Open **Add-ons**, enter one HTTPS Stremio add-on manifest URL per line, save, open a catalog title, and choose **Find streams**.

The prototype never executes add-on code. It sends native-direct HTTP(S) URLs straight to the Xbox player. Torrent-backed streams can be played through an explicitly configured external Stremio Service reachable from the Xbox; the app does not run a torrent engine on the console. Archives, proxy-header streams, and external pages remain classified but unavailable.

### Connect the streaming server

Open **Server** under **Developer / Advanced** in the left navigation. The default address is the local Stremio service at `http://192.168.1.105:11470/`. The app checks it on startup using Stremio's `/settings` endpoint; detailed status stays on the Server screen. Home shows a friendly reconnect notice only when disconnected or unreachable. **Connect and save** validates the response and remembers a changed address; **Disconnect server** disables server playback until you reconnect. A failed connection test preserves the previously saved address. Existing installs using the previous Render or `192.168.1.103:32768` defaults migrate automatically; custom addresses and explicit disconnection are preserved.

Keep the local server running and the Xbox on the same network. A streaming server supplies playback, while **Add-ons** or account synchronization supplies stream providers. Opening a title loads sources automatically after configuring providers; select a source to play it. Native direct streams also work when the server is unavailable.

## Validation

Use [docs/validation.md](docs/validation.md) to record comparable Edge and prototype results on each console tier. Do not treat a desktop build or debugger-attached console run as evidence that the Xbox resource limit is satisfied.

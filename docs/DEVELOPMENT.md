# Development

## Prerequisites

Use the prerequisites documented in the root [README](../README.md#build):

- Windows with Visual Studio 2026 and the required UWP tooling.
- Windows 11 SDK 10.0.26100.0.
- .NET 10 SDK.
- Desktop development with C++ workload for Release/NativeAOT packaging.
- Xbox Dev Mode and a development PC on the same network for console deployment.

Open `StremioXboxPrototype.sln` from the repository root. The application project is `src/StremioXboxPrototype/StremioXboxPrototype.csproj`.

## Build the application

From Developer PowerShell at the repository root:

```powershell
msbuild .\StremioXboxPrototype.sln /restore /p:Platform=x64 /p:Configuration=Debug
```

For the x64 Debug build used during development:

```powershell
msbuild .\StremioXboxPrototype.sln /t:Build /p:Configuration=Debug /p:Platform=x64 /m:2 /v:minimal
```

In Visual Studio, select the intended configuration/platform and deploy target before running. Local development commonly uses Debug / x64 / Local Machine. For Xbox, use the Remote Machine target and the console address shown in Xbox Dev Home.

## Run the standalone test harnesses

The repository currently contains lightweight executable test projects rather than a single test solution or a conventional test-runner setup.

```powershell
dotnet run --project .\tests\StreamSeederCount.Tests\StreamSeederCount.Tests.csproj
dotnet run --project .\tests\WatchHistory.Tests\WatchHistory.Tests.csproj
```

These projects compile selected application source files into standalone .NET console programs. A successful exit indicates that the assertions in that harness completed; it is not a substitute for an Xbox device test or a full end-to-end integration test.

## Xbox deployment

1. Enable Xbox Dev Mode and remote access in Dev Home.
2. Connect the PC and console to the same network.
3. In Visual Studio, choose x64 and Remote Machine.
4. Enter the Xbox console IP from Dev Home and complete the requested pairing/authentication.
5. Deploy and launch the app.
6. Validate controller navigation, focus visibility, layout, memory behavior and playback on the console itself.

The remote debugging endpoint is the console address. It is separate from the Stremio streaming-service URL configured inside the app.

See the root README's [Deploy to Xbox Dev Mode](../README.md#deploy-to-xbox-dev-mode) section for troubleshooting remote deployment and the Store package workflow.

## Store package workflow

The repository includes `tools/Build-StorePackage.ps1` for Store package creation. Read the root README's [Microsoft Store update](../README.md#build-a-microsoft-store-update) section before running it. The package identity and publisher must remain aligned with the existing Partner Center product. Update the package version according to the release requirements before packaging.

Development signing material and generated packages are local build artifacts. Do not commit private signing keys, passwords, certificates or generated package output.

## Useful scripts

| Script | Purpose |
|---|---|
| `tools/Build-StorePackage.ps1` | Build a Store submission package |
| `tools/Install-DevelopmentCertificate.ps1` | Create/install a local development signing certificate |
| `tools/Prepare-XboxSideload.ps1` | Prepare Xbox sideload workflow |
| `tools/Test-ProjectConfiguration.ps1` | Check project configuration |
| `tools/Generate-AppBranding.ps1` | Generate app branding assets |
| `tools/Generate-StoreAssets.ps1` | Generate Store artwork assets |
| `tools/rebrand-openstream-tv.ps1` | Apply the repository's OpenStream TV branding changes |

Inspect a script before running it, particularly if it changes package identity, signing configuration or generated assets.

## Change workflow

1. Check `git status --short` before editing and preserve existing user changes.
2. Inspect the XAML event handler and corresponding C# method together when changing UI behavior.
3. Keep controller focus paths explicit when adding or moving interactive controls.
4. Keep remote manifest and account data validation at service boundaries.
5. Check NativeAOT compatibility and JSON source-generation requirements when adding reflection-heavy APIs or serialized models.
6. Build the affected target and run the relevant test harnesses.
7. Run `git diff --check` and review `git diff` before committing.

## Troubleshooting pointers

- **XAML parse/startup errors:** inspect the reported resource key, style inheritance and XAML event handler names.
- **Manifest parse failures:** verify that the endpoint returns JSON manifest content rather than an HTML error page.
- **Account/session errors:** inspect account diagnostics and Windows Credential Locker access.
- **Server-dependent playback errors:** confirm the configured streaming service is reachable from the Xbox and check its status separately from add-on discovery.
- **Xbox-only focus or layout defects:** reproduce with a controller on the target console; desktop mouse behavior is not sufficient validation.
- **NativeAOT/Release failures:** verify the C++ linker workload, target RID and generated JSON serialization metadata.

For project architecture and add-on-specific behavior, see [Architecture](ARCHITECTURE.md) and [Add-ons](ADDONS.md).

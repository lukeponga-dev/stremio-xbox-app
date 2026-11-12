# Device validation worksheet

Record the OS build, app commit, network, display mode, and exact media URL or add-on response for every run.

## Xbox deployment preflight — 9 October 2026

- Modern UWP configuration restored: `net10.0-windows10.0.26100.0`, `WinExe`, `UseUwp=true`; Release uses Native AOT.
- Debug/x64 build: passed. Release/x64 Native AOT and Store MSIX validation: passed for version 1.0.8.0. All 74 local layout/history checks passed.
- The configured console address responded on Device Portal TCP 11443 with HTTP 401. This confirms the portal is reachable, but authentication is still required.
- Xbox sideload signing: passed after the approved development-certificate setup. The 1.0.8.0 x64 MSIX in `src/StremioXboxPrototype/AppPackages/XboxSideload_1.0.8.0_20261009-191916/` was signed and its CMS signature verified. The new self-signed certificate matches the package publisher and expires on 9 October 2029. The former PFX was backed up under ignored `SigningTemp/`.
- Not yet tested: certificate trust on the Xbox, signed-package installation, launch, controller flow, streaming, playback, suspension, memory, or Store acceptance. The console's Device Portal requires authentication (HTTP 401); do not mark any hardware rows below as passed from local builds.
- Next gate: authenticate to the Xbox Device Portal or pair Visual Studio's Remote Machine profile. In Device Portal's Apps manager, select the signed MSIX and its exported `.cer`; include `Dependencies-x64/Microsoft.VCLibs.x64.14.00.appx` if requested. Install, launch, and run the hardware checks below.

## Required hardware tiers

| Tier | Device | OS build | Display | Result owner |
|---|---|---|---|---|
| Original Xbox One |  |  |  |  |
| Xbox One S |  |  |  |  |
| Xbox One X |  |  |  |  |
| Xbox Series S |  |  |  |  |
| Xbox Series X |  |  |  |  |

## Browsing and controller checks

| Check | Target | Result | Evidence |
|---|---|---|---|
| Launch to usable Home | Agreed threshold |  |  |
| D-pad reaches every primary action | 100% |  |  |
| Focus remains visible at TV distance | 100% |  |  |
| Back returns without losing context | 100% |  |  |
| Focus restores to the previously opened card | 100% |  |  |
| Search usable with platform text entry | 100% |  |  |
| Controller keyboard supports letters, space, delete, clear, and Done | 100% |  |  |
| Voice search handles success, cancellation, missing microphone, and denied permission | Friendly recovery |  |  |
| Focused posters show purple border, 3% zoom, full title, and available metadata without clipping | 100% |  |  |
| Reduced-motion setting survives restart and preserves focus border | 100% |  |  |
| Safe margins, Settings advanced tools, and Back focus restoration remain reachable | 100% |  |  |
| Shelves show a deliberate next-card cue; horizontal focus reveals the whole selected card | Approximately 20% cue |  |  |
| Home loading skeletons, empty results, and retry state | Friendly recovery |  |  |
| Offline server reconnect notice preserves public catalog browsing | 100% |  |  |
| Login/profile state is readable at TV distance | 100% |  |  |
| Profile sync cannot be started twice | 100% |  |  |
| Sign out confirms and returns to Home | 100% |  |  |
| One failed add-on does not block other results | 100% |  |  |
| Large catalog remains responsive | Agreed threshold |  |  |

## Playback matrix

Run eligible rows in both Edge and the prototype under comparable conditions.

| Container / delivery | Video | Audio | DRM | Console | Open ms | Seek ms | Buffer events | 30-minute result | Notes |
|---|---|---|---|---|---:|---:|---:|---|---|
| MP4 progressive | H.264 | AAC | Clear |  |  |  |  |  |  |
| MP4 progressive | HEVC | AAC | Clear |  |  |  |  |  |  |
| MKV progressive | H.264 | AAC | Clear |  |  |  |  |  |  |
| MKV progressive | HEVC | Surround | Clear |  |  |  |  |  |  |
| HLS | H.264 | AAC | Clear |  |  |  |  |  |  |
| DASH | H.264 | AAC | Clear |  |  |  |  |  |  |
| DASH | HEVC | Surround | PlayReady |  |  |  |  |  |  |
| HLS/DASH | Any | Any | Widevine |  | Expected unsupported |  |  |  |  |

Also verify that X toggles mute/unmute, B returns from playback, and console system volume is audible before recording an audio failure.

For every applicable row, also test pause, resume, repeated seek, audio-track selection, embedded subtitles, external subtitles, expired URLs, network loss, app suspension, and loss of an external streaming service.

## Resource and recovery checks

| Check | Target | Result | Evidence |
|---|---|---|---|
| Foreground memory, non-debug | Below configured Xbox app limit |  |  |
| Memory after repeated browse/play cycles | No unbounded growth |  |  |
| Background/suspended behavior | Predictable save and recovery |  |  |
| Network loss | Actionable error and retained context |  |  |
| Unsupported stream | Explanation and return to selection |  |  |
| External service unavailable | Fast reachability failure and recovery |  |  |

## Decision record

- Native-direct coverage:
- External-service-dependent coverage:
- Unsupported coverage:
- Controller acceptance:
- Resource acceptance:
- Store/distribution finding:
- Shared-core finding:
- Recommendation: **GO / CONDITIONAL GO / NO-GO**

# Design QA

## Full-window TV layout — 7 October 2026

- Full-window bounds are selected before first-page navigation. The frame and pages explicitly stretch horizontally and vertically.
- Home's fixed outer padding is removed. Sidebar and page backgrounds extend to every window edge; focusable content uses responsive five-percent insets to account for TV overscan.
- Layout follows XAML effective viewport dimensions after system scaling, rather than assuming a TV's physical resolution. Size changes recalculate insets, header placement, search controls, keyboard height, and detail poster visibility.
- Small viewports stack header/search actions, make browse suggestions scrollable, and give details the full content width. Detail action buttons, episode selection, status copy, and source rows use available width.
- Playback fills the window with the native player. Video retains its aspect ratio; status overlays wrap, scroll when necessary, and remain bounded inside the viewport.

Validation: all 47 watch-history and TV-layout checks passed, including 640 × 360, Xbox-effective 960 × 540, 720p, 1080p, 1440p, 4K, 8K, and ultrawide layout inputs. Unsigned x64 Debug build passed. These checks verify layout calculations and compilation, not rendered device screenshots.

Native application capture is unavailable in this session. Hardware validation remains necessary for controller focus, actual display scaling, TV overscan settings, and visual clipping.

final result: blocked (runtime visual verification)

## Home refinement — 7 October 2026

Scope: improve the native UWP Home screen using the supplied Nexa board and the review's recommendations. Existing branding and navigation remain in use.

- Home shelves use larger cards, larger title/metadata text, a white focus outline, and a restrained 3% scale inside the card gutter. Motion-disabled focus retains the outline.
- Continue Watching leads the shelves when local history exists. Real progress bars and time remaining appear without requiring focus. First-run history stays hidden.
- Playback saves position every 15 seconds and on leaving the player. Selecting a title returns to its details and remembered episode, requests fresh provider sources, and resumes when the chosen source supports seeking. Completion removes the title. Playback-lab URLs do not enter history.
- Saved progress stays usable while public catalogs load or fail. The Home header provides Refresh for recovery.
- History stores a compact title snapshot per entry, bounded to twelve titles, without retaining playback URLs.

Validation: unsigned x64 Debug build passed; all 20 watch-history/TV-layout checks passed against production settings, serialization, and layout logic using an in-memory storage adapter. XAML XML parsing and diff whitespace checks passed. Signed packaging fails on the pre-existing local signing certificate.

Visual/controller validation remains blocked: this session cannot control or capture native Windows applications. The supplied board was inspected, but a matching runtime screenshot and hardware controller checks could not be produced. Verify sofa-distance readability, focus restoration, shelf scrolling, episode selection, seekable resume, and completed-title removal on Xbox before release.

final result: blocked (runtime visual verification)

## TV update — 5 October 2026

The current TV brief supersedes the compact desktop reference below.

- Search is a compact header action. Discover owns the text field, controller keyboard, optional Windows voice recognition, search results, and friendly empty/error messages. Keyboard rows scroll when the available height is small.
- Home, Discover, Library, and Add-ons are primary navigation. Playback Lab, Server, Diagnostics, and Settings occupy the lower Developer / Advanced group. The sidebar scrolls when necessary.
- Poster artwork keeps a 2:3 ratio with rounded corners and titles underneath. Focus adds a white border, violet halo, 6% scale, and year/rating/runtime metadata when supplied by Cinemeta. Metadata requests are delayed, cancelled on focus changes, and cached. Settings can disable animation.
- Main and detail content have 48px horizontal / 32px vertical safe spacing; playback overlays share those insets. Shelves adapt their card width to leave a 20% next-card cue at the initial scroll position.
- Home has poster skeletons, empty and retry states, and a friendly reconnect notice when the playback server is unavailable. Catalog counts and technical server errors are confined to Diagnostics and Server.

Validation: the x64 Debug build passed. A temporary console harness exercised the production shelf-sizing helper across 960, 1280, 1920, 2560, and 3840px display widths, checking the 20% cue and focus allowance. Metadata deserialization, library round trips, and missing-metadata fallback passed. Live Cinemeta metadata for The Matrix returned year 1999, rating 8.7, and runtime 136 min. The configured server is https://watchstream-stremio-server.onrender.com/.

Xbox runtime visuals, controller navigation, keyboard input, speech/microphone permissions, and playback still require device validation. Existing packaging-certificate warnings remain. The desktop-reference findings below describe an earlier layout.
## Comparison target

- Source visual truth: `C:\Users\lukeg\Downloads\stremio-ui.png`
- Source pixels: 1920 × 900
- Intended implementation viewport: 1920 × 900 desktop/Xbox layout
- State: Home with populated movie and series shelves

## Findings addressed

- [P1] The previous wide, text-labelled sidebar did not match the reference's compact icon rail. Replaced it with a narrow, labelled-on-hover icon rail while preserving every navigation action.
- [P1] The previous page title/header hierarchy did not match the reference. Replaced it with the centered rounded search surface and compact account/full-screen actions.
- [P1] Catalog shelves were too spaced out and lacked the reference's shelf headers. Updated the poster dimensions, title treatment, shelf titles, "See all" affordances, and horizontal scrollbars.
- [P2] The previous blue/steel token set diverged from the reference. Updated the shared brushes to the reference's deep indigo and violet palette.

## Required fidelity surfaces

- Fonts and typography: uses the native UWP system font with increased shelf-title and poster-title sizing to match the reference hierarchy.
- Spacing and layout rhythm: compact 88px rail, 480px centered search, 160 × 238 poster art, 24px item gutters, and two horizontal shelves.
- Colors and visual tokens: deep indigo canvas with violet active-state and search-surface tokens.
- Image quality and asset fidelity: existing live catalog poster URLs remain in place; no placeholder artwork was introduced.
- Copy and content: the catalog labels now read `Movies - Popular`, `Series - Popular`, `See all`, and `Search or paste link` as in the reference.

## Focused comparison

The reference image was opened and inspected. A runtime capture of the UWP application could not be produced in this environment: `dotnet build` does not have the UWP XAML generation/packaging target available and therefore fails before an executable is generated (missing generated `InitializeComponent` and control symbols across the pre-existing app). The edited XAML files were XML-parsed successfully, and `git diff --check` passed.

## Implementation checklist

- [x] Implement compact left icon navigation.
- [x] Implement centered search and top-right actions.
- [x] Implement horizontal poster shelves with reference-aligned headings.
- [x] Preserve existing catalog search, navigation, and item-click behavior.
- [ ] Launch and compare the compiled UWP package on a Windows machine with the UWP build workload.

final result: blocked

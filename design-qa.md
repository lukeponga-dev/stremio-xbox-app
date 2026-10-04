# Design QA

## TV update — 5 October 2026

Follow-up: the app now requests full-screen mode at launch and uses core-window bounds to remove the platform's visible-bounds inset. The main-page outer padding has been removed, so the sidebar reaches the screen edges. Content uses 24px horizontal spacing, and settings panels and the details description expand to the available width. Full-screen appearance on Xbox still needs runtime verification.

The TV request supersedes the compact desktop reference below. Navigation now has visible labels, posters measure 184 × 276 with 20px titles, inputs have a 60px minimum height, and the main page has 48px horizontal / 36px vertical screen-edge padding. Search and server status occupy separate header rows without overlapping controls. Details stream rows stretch to the available width instead of forcing a 700px minimum. Controller Back returns from a main-page section to Home.

The default server is `http://192.168.1.103:32768/`. A dedicated Server screen supports connection validation, saving, disconnecting, startup checks, and visible errors. The production connection client successfully read Stremio 4.22.0 from this server. Response acceptance, invalid JSON/schema, HTTP failure, and cancellation checks passed in a temporary console harness. Visual Studio MSBuild completed the x64 Debug build; existing signing-certificate warnings remain.

Runtime visual inspection and controller navigation on Xbox remain unverified. The desktop-reference findings below describe the previous layout.

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

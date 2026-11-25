# Add-ons

This document describes the Add-ons screen and provider workflows implemented in `MainPage.xaml` and `MainPage.xaml.cs`.

## Screen structure

The `AddonsPanel` in `MainPage.xaml` contains:

- The Add-ons heading and `AddonCountText` provider count.
- `AddonSyncAccountButton` to synchronize stream add-ons from a signed-in Stremio account.
- `AddProviderButton` to show or hide the manifest import form.
- `AddonUrlsBox` for one or more manifest URLs.
- `SaveProvidersButton` to validate and save providers.
- `SyncProvidersButton` to open the account area.
- `AddonSaveStatus` and `AddonSummaryText` for operation feedback.
- `AddonCards`, a vertical `ListView` of provider cards.

The provider list is deliberately vertical. `UpdateAddonGridLayout` uses the shared `TvLayout` helper and explicitly links important controls with X/Y focus targets. Pointer movement over a provider row also moves pointer focus to that row.

## Provider card data

`RenderAddonCards()` builds a list of `AddonCardView` objects and assigns it to `AddonCards.ItemsSource`.

The built-in Cinemeta catalog is always represented as a built-in entry. Configured stream providers are read from `PrototypeSettings.GetStreamAddons()`. If manifest metadata has been loaded, the card uses the manifest name, version and description; otherwise it falls back to the manifest host and a generic provider description.

The card template in XAML binds to the view model properties such as `Name`, `Category`, `Description` and `Version`. The logo is only used when a manifest supplies a valid HTTPS logo URI. The built-in catalog is not removable through the provider removal action.

The current status/category labels distinguish built-in Cinemeta from configured stream providers; they should not be interpreted as a live provider health check or an enabled/disabled state.

## Importing a manifest

The import form accepts manifest URLs, parsed by `PrototypeSettings.ParseStreamAddons`. `SaveAddons` then:

1. Rejects empty input and input that produces no candidate URLs.
2. Requests each candidate manifest through `StremioAddonClient.GetManifestAsync`.
3. Checks that the manifest declares a `stream` resource (either as a string resource or an object with a `name` of `stream`).
4. Keeps valid manifests in an accepted list and caches their metadata in memory.
5. Merges accepted providers with existing local providers and de-duplicates by absolute manifest URL, case-insensitively.
6. Persists the resulting list through `PrototypeSettings.SetStreamAddons`.
7. Refreshes the count and provider cards.

Invalid candidates are logged to diagnostics. If no candidate is accepted, the existing saved provider list is retained. Importing does not execute provider code; the app communicates with the manifest's HTTP endpoints using its own client.

## Loading metadata

When the Add-ons screen is shown, `LoadAddonMetadataAsync` requests manifest metadata for Cinemeta and configured providers that are not already in the in-memory metadata dictionary. Requests are made sequentially. The request is cancelled when superseded or when the user navigates away from the Add-ons panel.

Each successful manifest response is cached in `_addonMetadata` and triggers a card refresh. A metadata request failure is recorded as a warning; the configured provider remains in the saved list.

A logo is accepted only when the manifest logo resolves to an HTTPS URI. If the manifest has no usable logo, the card has no remote logo image.

## Account synchronization

The Add-ons screen's Sync Add-ons action checks for an existing account session. If there is no session, it opens the account screen. If a session exists, it enters the account sync flow.

`SyncAccountAddonsAsync` retrieves the account's add-on collection through `StremioAccountClient.GetAddonCollectionAsync`. It considers HTTPS transport URLs, uses an account-provided manifest when present (otherwise requests the manifest), and keeps entries declaring a stream resource. It then replaces the locally saved stream add-on list with the stream-capable add-ons returned by the account collection.

This differs from manual import: manual import merges new providers with existing local providers, while account synchronization replaces the local stream-provider list with the eligible account collection. The built-in Cinemeta catalog remains separately represented by the UI.

## Details, copy and removal

Selecting a provider card opens a details dialog with its category, version (when available), description and manifest URL. The dialog offers copying the URL and, for removable configured providers, a removal action. Removal requires confirmation and updates the locally saved list and in-memory metadata cache.

The direct remove button also confirms before removing a configured provider. The built-in Cinemeta entry is not removable.

## Special Comet description handling

`GetAddonDescription` detects manifest descriptions containing the known obsolete-configuration warning phrases `OBSOLETE CONFIGURATION` or `PLEASE RE-CONFIGURE`. It replaces the card description with guidance to generate a fresh manifest from Comet. This is presentation-only: it does not modify the remote provider, rewrite the manifest URL or automatically remove the existing configuration.

## Troubleshooting

| Symptom | What to check |
|---|---|
| Provider is shown by host name | The manifest metadata has not loaded, or the manifest name is unavailable |
| Description/version is missing | Check the manifest response and Diagnostics |
| Provider is saved but returns no streams | Confirm the manifest declares a stream resource and inspect stream requests from the details flow |
| Manifest import is rejected | Confirm the URL is valid HTTPS and returns a valid Stremio manifest with a stream resource |
| Account sync reports an error | Check the account session, network connectivity and Diagnostics |
| Logo is absent | Confirm the manifest has a valid HTTPS logo URI |

Relevant implementation files:

- `src/StremioXboxPrototype/MainPage.xaml`
- `src/StremioXboxPrototype/MainPage.xaml.cs`
- `src/StremioXboxPrototype/Services/PrototypeSettings.cs`
- `src/StremioXboxPrototype/Services/StremioAddonClient.cs`
- `src/StremioXboxPrototype/Services/StremioAccountClient.cs`

# Stremio Xbox Privacy Policy — Draft for Review

**Effective date:** 5 October 2026

Stremio Xbox is a media-browsing application for Xbox and Windows. This draft describes the data handling implemented in the current application build. Before publishing, replace the bracketed contact details and have this policy reviewed for the jurisdictions where the app will be offered.

## Information handled by Stremio Xbox

Stremio Xbox can be used without creating an account. If a user chooses to connect a compatible media-provider account, Stremio Xbox handles the following information to provide sign-in, account synchronization, and the user's media experience:

- Account email address.
- A provider-issued session/authentication key.
- Account profile information returned by the provider, including a profile identifier, avatar URL, and add-on count.
- The user's configured compatible add-on endpoints and streaming-service URL.
- The media items a user saves to their local library.

## How information is used

Stremio Xbox uses this information only to:

- authenticate the user with their chosen compatible media provider;
- restore a signed-in session and retrieve the user's provider profile and add-on collection;
- request media catalog, stream, or playback information from the provider, compatible add-ons, and the streaming-service endpoint selected by the user; and
- retain the user's local settings and saved library on that device.

Stremio Xbox does not include analytics, advertising SDKs, or a separate Stremio Xbox account service in the current build.

## Sharing and network requests

When the user signs in, Stremio Xbox sends the credentials or provider-issued login token to the selected compatible provider. Provider sign-in may also open the provider's secure sign-in page in Microsoft Edge.

Stremio Xbox sends requests to the compatible provider, add-on endpoints, and streaming-service URL configured by the user. Those third parties process information under their own privacy policies. Stremio Xbox does not control those third-party services.

## Storage and security

The provider session key is stored in Windows Credential Locker. Stremio Xbox stores the user's email address, profile cache, configured endpoint URLs, and saved-library data in Windows local application settings on the device. The user can sign out to remove the locally stored provider session and profile cache. Removing the application may remove other locally stored app settings, subject to the device's operating-system behavior.

## Children's privacy

Stremio Xbox is not directed to children. The application does not knowingly collect personal information from children for its own use. Parents and guardians should supervise a child's use of compatible providers and third-party services.

## Changes to this policy

We may update this policy when Stremio Xbox's features or data practices change. The effective date at the top of this policy will be updated when changes are made.

## Contact

For privacy questions or requests, contact: **[insert Stremio Xbox privacy contact email or support URL before publication]**.

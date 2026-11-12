# watchstream Privacy Policy

**Effective date: 6 October 2026**

Kiwi Cloud ("we", "us", or "our") publishes **watchstream**, an Xbox and Windows app for browsing media information and playing streams supplied by Stremio services and user-selected add-ons. This policy explains what information watchstream handles, why it is handled, where it goes, and the choices available to you.

## Information handled by the app

watchstream may handle the following information when you choose to use the related feature:

- **Stremio account information.** If you sign in with email and password, the app sends those credentials directly to Stremio's account service to authenticate you. The password is not retained by watchstream. The returned Stremio session key is stored in Windows Credential Locker. The app also stores your Stremio email address, account identifier, avatar URL, and synchronized add-on count locally so it can display your signed-in profile.
- **Facebook sign-in information.** If you choose Facebook sign-in, the sign-in page opens through Stremio in Microsoft Edge. watchstream does not receive or store your Facebook password. The app receives the resulting Stremio login token and uses it to create a Stremio session.
- **Voice search.** If you choose Voice search, Windows speech recognition processes microphone input and returns recognized text to the app. watchstream does not record or retain microphone audio. The recognized search text is used to request matching media information.
- **Search and media requests.** Search terms, media identifiers, catalog selections, stream identifiers, and playback requests are sent to Cinemeta, Stremio, the add-ons you configure, and the configured streaming server as needed to provide the feature you requested.
- **Locally saved information.** Your library, configured add-on addresses, streaming-server address, display preferences, cached profile details, and account email address are stored in the app's local Windows storage. Your Stremio session key is stored separately in Windows Credential Locker.
- **Technical diagnostics.** The app keeps a short, in-memory list of recent status and error messages for its Diagnostics screen. These entries can include request hostnames, media identifiers, response status, and timing information. They are cleared when the app closes and are not sent to Kiwi Cloud automatically.
- **Network information.** Internet services contacted by the app can receive information normally sent with a network request, including your IP address, request time, and requested resource.

watchstream does not include advertising or its own analytics service. Kiwi Cloud does not sell personal information handled by the app.

## How information is used

The app uses this information only to:

- sign you in to Stremio and maintain your session;
- display and synchronize your Stremio profile and add-ons;
- search and display media catalogs and metadata;
- find, prepare, and play streams you select;
- remember your library and app settings on your device;
- provide voice search when requested; and
- show local diagnostic information when a feature fails.

## Services that receive information

Depending on the features you use, information may be disclosed to these types of service providers:

- **Stremio services** for account authentication, profile access, add-on synchronization, catalog information, metadata, and streaming-server functions;
- **Facebook and Microsoft Edge** when you choose the Facebook sign-in flow;
- **Microsoft Windows speech services** when you choose voice search;
- **Cinemeta and user-configured Stremio add-ons** for catalog, search, metadata, and stream requests;
- **Render**, which hosts the default watchstream Stremio streaming server and processes the technical request information needed to operate that service; and
- **network and infrastructure providers** that transmit or host these requests.

These third parties process information under their own terms and privacy policies. Add-ons are selected by you or synchronized from your Stremio account. Kiwi Cloud does not control independently operated add-ons and recommends reviewing their policies before using them.

## Storage, security, and retention

The app uses HTTPS for its default online services and requires user-configured add-on manifests to use HTTPS. If you manually configure an HTTP streaming server, traffic to that server is not encrypted.

The Stremio session key is protected by Windows Credential Locker. Other saved app information remains in the app's local storage on your device. It stays there until you remove it, reset or uninstall the app, or the app replaces it during normal use. In-memory diagnostics are discarded when the app closes.

Third-party services may retain request and account information according to their own policies. Kiwi Cloud does not operate Stremio, Cinemeta, Facebook, Microsoft speech services, or independently configured add-ons.

## Your choices and controls

You control whether to sign in, use Facebook sign-in, enable microphone access, use voice search, configure add-ons, configure a streaming server, save titles to the local library, and start playback.

- Choose **Sign out** to remove the saved Stremio session key and cached profile information from the app and request logout from Stremio.
- Choose **Disconnect server** to remove the configured streaming-server connection.
- Remove titles from **Library** to delete those locally saved entries.
- Change or remove add-on addresses from **Add-ons**.
- Turn microphone permission off in Windows or Xbox privacy settings to prevent voice-search access.
- Reset or uninstall watchstream to remove its remaining local app data.
- Use Stremio's account tools to access, correct, or delete information held by Stremio.

To ask Kiwi Cloud about information controlled by watchstream, open a support request at <https://github.com/lukeponga-dev/stremio-xbox-prototype/issues>. Requests concerning information controlled by Stremio, Microsoft, Facebook, Render, or an independent add-on should be directed to that provider.

## Children

watchstream is not designed to knowingly collect personal information from children. Account sign-in and third-party services remain subject to the age requirements and parental controls of those services and the Microsoft/Xbox platform.

## Changes to this policy

We may update this policy when watchstream's features or data practices change. The effective date at the top identifies the current version. Material changes will be reflected in the policy made available through the Microsoft Store listing.

## Contact

**Kiwi Cloud**  
Privacy and support requests: <https://github.com/lukeponga-dev/stremio-xbox-prototype/issues>

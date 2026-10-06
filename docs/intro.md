## This app is a C# UWP/XAML project. Open [StremioXboxPrototype.sln](C:/Users/lukeg/source/repos/lukeponga-dev/stremio-xbox-prototype/StremioXboxPrototype.sln) in Visual Studio.

### The main files are:

- [MainPage.xaml](C:/Users/lukeg/source/repos/lukeponga-dev/stremio-xbox-prototype/src/StremioXboxPrototype/MainPage.xaml:1) — Home, Discover, Library, sidebar, Server, Settings, and Diagnostics layout.
- [MainPage.xaml.cs](C:/Users/lukeg/source/repos/lukeponga-dev/stremio-xbox-prototype/src/StremioXboxPrototype/MainPage.xaml.cs:1) — button actions, navigation, loading catalogs, search, account handling, and server connection logic.
- [App.xaml](C:/Users/lukeg/source/repos/lukeponga-dev/stremio-xbox-prototype/src/StremioXboxPrototype/App.xaml:1) — shared colors, button styles, typography, and focus appearance.
- [PosterCard.xaml](C:/Users/lukeg/source/repos/lukeponga-dev/stremio-xbox-prototype/src/StremioXboxPrototype/Controls/PosterCard.xaml:12) — poster dimensions, title placement, focus border, glow, and scaling.
- [DetailsPage.xaml](C:/Users/lukeg/source/repos/lukeponga-dev/stremio-xbox-prototype/src/StremioXboxPrototype/DetailsPage.xaml:1) — movie and series details.
- [PlayerPage.xaml](C:/Users/lukeg/source/repos/lukeponga-dev/stremio-xbox-prototype/src/StremioXboxPrototype/PlayerPage.xaml:1) — video player interface.
- [PrototypeSettings.cs](C:/Users/lukeg/source/repos/lukeponga-dev/stremio-xbox-prototype/src/StremioXboxPrototype/Services/PrototypeSettings.cs:9) — default server address, saved add-ons, library, and user settings.
- [Package.appxmanifest](C:/Users/lukeg/source/repos/lukeponga-dev/stremio-xbox-prototype/src/StremioXboxPrototype/Package.appxmanifest:8) — app identity, version, Store publisher, capabilities, and package information.

For example, to change the sidebar wording, edit the button `Content` values near the beginning of `MainPage.xaml`. To change poster size, edit the `Width="216"` and `Height="324"` values in `PosterCard.xaml`. Keep those dimensions at a 2:3 ratio.

### To change the built-in streaming server, update:

```csharp
public const string DefaultStreamingServiceUrl =
    "http://192.168.1.105:11470/";
```

in `PrototypeSettings.cs`. Existing users who previously saved another server may keep their saved address until they reconnect or reset the app.

### To test an edit:

1. Open the solution in Visual Studio.
2. Select **Debug**, **x64**, and **Local Machine**.
3. Press **F5**.
4. For Xbox testing, change the target to **Remote Machine** and select the console IP shown in Xbox Dev Home.

### For the next Store release:

1. Change the version in `Package.appxmanifest` from `0.1.3.0` to `0.1.4.0`.
2. Select **Release** and **x64**.
3. Use **Publish → Create App Packages**.
4. Build the Microsoft Store package.
5. Upload the new package to the existing Partner Center submission.
6. Update the release notes and resubmit it for certification.

Keep the Store identity `pongadev.watchstream` and publisher information `Kiwi Cloud` unchanged unless Partner Center gives you replacement identity values. Avoid editing generated or compiled folders such as `bin`, `obj`, and `AppPackages`.

### To publish code changes, create a higher-versioned MSIX package and submit it as an update to the existing watchstream product.

1. Make and test your changes in Visual Studio:
   - Open [StremioXboxPrototype.sln](C:/Users/lukeg/source/repos/lukeponga-dev/stremio-xbox-prototype/StremioXboxPrototype.sln).
   - Select Debug, x64, Local Machine.
   - Build and run the app.
   - Test controller focus, navigation, playback, and server connection.
2. Increase the package version in [Package.appxmanifest (line 8)](C:/Users/lukeg/source/repos/lukeponga-dev/stremio-xbox-prototype/src/StremioXboxPrototype/Package.appxmanifest:8).
   For the next update, use:
   Version="1.0.0.0"
   Microsoft reserves the fourth version number for Store use, so keep the final number at 0. Each uploaded update must have a higher applicable version. Microsoft package-version requirements
3. Keep these Store identity values unchanged:
   Name="pongadev.watchstream"
   Publisher="CN=..."
   Also keep PublisherDisplayName as:
   <PublisherDisplayName>Kiwi Cloud</PublisherDisplayName>
4. Create the Store package in Visual Studio:
   - Select Release and x64.
   - Right-click the project.
   - Choose Publish → Create App Packages.
   - Select the package for the app already associated with watchstream.
   - Build an x64 package.
   - Let Visual Studio run package validation.
   Visual Studio will create an .msix or .msixupload file under the project’s AppPackages directory. Microsoft packaging instructions
5. Open watchstream → Application overview in Partner Center.
   - If the current submission is still in certification, either wait for it to finish or cancel certification before replacing it.
   - After the current release is published, select Start update.
   - Open Packages.
   - Remove superseded draft packages if necessary.
   - Upload the newly generated package.
   - Wait until Partner Center reports Validated and Complete.
6. Update the Store listing:
   - Add concise release notes explaining the changes.
   - Replace screenshots if the interface changed.
   - Keep no more than seven keywords.
   - Do not add Stremio as a keyword because Microsoft already rejected that search term.
7. Check that every submission section says Complete, then select Submit for certification. Microsoft will review the update, and after approval it will replace the previous Store release. Existing users receive the higher-versioned package through Microsoft Store updates. Microsoft’s app-update process
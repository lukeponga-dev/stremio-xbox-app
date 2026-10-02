using System.Collections.ObjectModel;
using StremioXboxPrototype.Models;
using StremioXboxPrototype.Services;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

namespace StremioXboxPrototype;

public sealed partial class MainPage : Page
{
    private readonly StremioAddonClient _client = new();
    private readonly StremioAccountClient _accountClient = new();
    private readonly StremioStreamingServiceClient _streamingServiceClient = new();
    private CancellationTokenSource? _request;
    private readonly DispatcherTimer _memoryTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private Control? _lastFocusedItem;
    private bool _isSignedIn;
    private bool _isSyncing;

    public ObservableCollection<DiagnosticEntry> Diagnostics => DiagnosticsService.Current.Entries;

    public MainPage()
    {
        InitializeComponent();
        AddonUrlsBox.Text = PrototypeSettings.GetStreamAddonText();
        StreamingServiceUrlBox.Text = PrototypeSettings.GetStreamingServiceUrlText();
        _memoryTimer.Tick += (_, _) => UpdateMemory();
        SystemNavigationManager.GetForCurrentView().BackRequested += BackRequested;
        Loaded += PageLoaded;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (_lastFocusedItem is not null)
        {
            _lastFocusedItem.Focus(FocusState.Programmatic);
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _request?.Cancel();
        base.OnNavigatedFrom(e);
    }

    private async void PageLoaded(object sender, RoutedEventArgs e)
    {
        var cachedProfile = PrototypeSettings.GetProfileCache();
        if (cachedProfile is not null) UpdateAccountNavigation(true, cachedProfile.Email, cachedProfile.Id, cachedProfile.AddonCount, cachedProfile.Avatar);
        try
        {
            var session = await _accountClient.RestoreSessionAsync();
            if (session is not null)
            {
                AccountEmailBox.Text = session.Email;
                SetAccountStatus($"Signed in as {session.Email}. Session verified by Stremio.", AccountStatus.Success);
                UpdateAccountNavigation(true, session.Email, cachedProfile?.Id, cachedProfile?.AddonCount ?? 0, cachedProfile?.Avatar);
            }
            else
            {
                SetAccountStatus("Not signed in", AccountStatus.Neutral);
                UpdateAccountNavigation(false);
            }
        }
        catch (Exception exception)
        {
            SetAccountStatus("Saved sign-in could not be verified. Check your connection, then try Sync add-ons.", AccountStatus.Error);
            DiagnosticsService.Current.Warn("account", "Session verification failed: " + exception.Message);
        }
        HomeButton.Focus(FocusState.Programmatic);
        if (MovieGrid.Items.Count == 0) await LoadHomeAsync();
    }

    private async Task LoadHomeAsync()
    {
        BeginRequest("Loading public catalogs…");
        try
        {
            var movies = _client.GetCatalogAsync("movie", cancellationToken: _request!.Token);
            var series = _client.GetCatalogAsync("series", cancellationToken: _request.Token);
            await Task.WhenAll(movies, series);
            MovieGrid.ItemsSource = movies.Result.Take(20).ToList();
            SeriesGrid.ItemsSource = series.Result.Take(20).ToList();
            NetworkStatusText.Text = $"Loaded {movies.Result.Count + series.Result.Count} catalog items";
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            NetworkStatusText.Text = "Catalog unavailable";
            await ShowErrorAsync("Catalog unavailable", exception.Message);
        }
        finally
        {
            EndRequest();
        }
    }

    private async void Search(object sender, RoutedEventArgs e) => await SearchAsync();

    private async void SearchBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter) await SearchAsync();
    }

    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchBox.Text)) return;
        SetSelectedNavigation(DiscoverButton);
        ShowPanel(DiscoverPanel, "Discover", "Search Cinemeta using the Stremio add-on protocol");
        BeginRequest("Searching…");
        try
        {
            var movies = _client.GetCatalogAsync("movie", search: SearchBox.Text, cancellationToken: _request!.Token);
            var series = _client.GetCatalogAsync("series", search: SearchBox.Text, cancellationToken: _request.Token);
            await Task.WhenAll(movies, series);
            DiscoverGrid.ItemsSource = movies.Result.Concat(series.Result).ToList();
            NetworkStatusText.Text = $"{DiscoverGrid.Items.Count} search results";
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            await ShowErrorAsync("Search failed", exception.Message);
        }
        finally
        {
            EndRequest();
        }
    }

    private void OpenItem(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not MetaItem item) return;
        _lastFocusedItem = FocusManager.GetFocusedElement() as Control;
        Frame.Navigate(typeof(DetailsPage), item);
    }

    private void PlayDirectUrl(object sender, RoutedEventArgs e)
    {
        if (!Uri.TryCreate(DirectUrlBox.Text.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            _ = ShowErrorAsync("Invalid media URL", "Enter an absolute HTTP or HTTPS URL.");
            return;
        }
        Frame.Navigate(typeof(PlayerPage), new PlaybackRequest(uri, "Playback lab", "Manual direct URL"));
    }

    private async void SaveAddons(object sender, RoutedEventArgs e)
    {
        PrototypeSettings.SetStreamAddonText(AddonUrlsBox.Text);
        var candidates = PrototypeSettings.GetStreamAddons();
        if (candidates.Count == 0)
        {
            AddonSaveStatus.Text = "Enter at least one HTTPS manifest URL.";
            return;
        }

        BeginRequest("Validating add-on manifests…");
        var accepted = new List<AddonEndpoint>();
        try
        {
            foreach (var candidate in candidates)
            {
                try
                {
                    var manifest = await _client.GetManifestAsync(candidate.ManifestUri, _request!.Token);
                    if (!manifest.Resources.Any(resource =>
                            resource.ValueKind == System.Text.Json.JsonValueKind.String && resource.GetString() == "stream" ||
                            resource.ValueKind == System.Text.Json.JsonValueKind.Object &&
                            resource.TryGetProperty("name", out var name) && name.GetString() == "stream"))
                    {
                        DiagnosticsService.Current.Warn("settings", $"{manifest.Name} has no stream resource");
                        continue;
                    }
                    accepted.Add(new AddonEndpoint(manifest.Name, candidate.ManifestUri));
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    DiagnosticsService.Current.Warn("settings", $"Rejected {candidate.ManifestUri.Host}: {exception.Message}");
                }
            }

            PrototypeSettings.SetStreamAddons(accepted);
            AddonUrlsBox.Text = PrototypeSettings.GetStreamAddonText();
            AddonSaveStatus.Text = accepted.Count == 0
                ? "No valid stream add-on manifests were found. See Diagnostics."
                : $"Saved {accepted.Count}: {string.Join(", ", accepted.Select(addon => addon.Name))}.";
            DiagnosticsService.Current.Info("settings", $"Validated and saved {accepted.Count} stream add-ons");
        }
        catch (OperationCanceledException) { }
        finally
        {
            EndRequest();
        }
    }

    private async void SaveStreamingService(object sender, RoutedEventArgs e)
    {
        if (!Uri.TryCreate(StreamingServiceUrlBox.Text.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            StreamingServiceStatus.Text = "Enter an absolute HTTP or HTTPS Stremio Service URL.";
            return;
        }

        BeginRequest("Testing Stremio Service…");
        try
        {
            await _streamingServiceClient.TestAsync(uri, _request!.Token);
            PrototypeSettings.SetStreamingServiceUrl(uri);
            StreamingServiceUrlBox.Text = PrototypeSettings.GetStreamingServiceUrlText();
            StreamingServiceStatus.Text = $"Connected to {uri.Host}. Torrent-backed streams can now be selected.";
            DiagnosticsService.Current.Info("streaming-service", $"Connected to {uri.Host}");
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            StreamingServiceStatus.Text = "Service unavailable: " + exception.Message;
            DiagnosticsService.Current.Error("streaming-service", exception.Message);
        }
        finally { EndRequest(); }
    }

    private async void SignInAndSync(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(AccountEmailBox.Text) || string.IsNullOrEmpty(AccountPasswordBox.Password))
        {
            SetAccountStatus("Enter your Stremio email and password.", AccountStatus.Error);
            return;
        }

        BeginRequest("Signing in to Stremio…");
        try
        {
            var login = await _accountClient.LoginAsync(AccountEmailBox.Text, AccountPasswordBox.Password, _request!.Token);
            AccountPasswordBox.Password = "";
            UpdateAccountNavigation(true, login.User?.Email ?? AccountEmailBox.Text, login.User?.Id, 0, login.User?.Avatar);
            SetAccountStatus($"Signed in as {login.User?.Email ?? AccountEmailBox.Text}. Syncing add-ons…", AccountStatus.Neutral);
            _isSyncing = true;
            SyncAddonsButton.IsEnabled = false;
            await SyncAccountAddonsAsync(login.AuthKey, _request.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            AccountPasswordBox.Password = "";
            SetAccountStatus("Sign-in failed: " + exception.Message, AccountStatus.Error);
            DiagnosticsService.Current.Error("account", exception.Message);
        }
        finally { _isSyncing = false; SyncAddonsButton.IsEnabled = true; EndRequest(); }
    }

    private async void SignInWithFacebook(object sender, RoutedEventArgs e)
    {
        BeginRequest("Starting Facebook sign-in…");
        try
        {
            var attempt = _accountClient.CreateFacebookLoginAttempt();
            SetAccountStatus("Complete Facebook sign-in in Microsoft Edge, then return to this app.", AccountStatus.Neutral);
            if (!await Launcher.LaunchUriAsync(attempt.LoginUri))
                throw new InvalidOperationException("Xbox could not open the Stremio Facebook sign-in page.");

            var login = await _accountClient.CompleteFacebookLoginAsync(attempt.State, _request!.Token);
            AccountEmailBox.Text = login.User?.Email ?? "";
            UpdateAccountNavigation(true, login.User?.Email ?? "Facebook user", login.User?.Id, 0, login.User?.Avatar);
            SetAccountStatus($"Signed in as {login.User?.Email ?? "Facebook user"}. Syncing add-ons…", AccountStatus.Neutral);
            _isSyncing = true;
            SyncAddonsButton.IsEnabled = false;
            await SyncAccountAddonsAsync(login.AuthKey, _request.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            SetAccountStatus("Facebook sign-in failed: " + exception.Message, AccountStatus.Error);
            DiagnosticsService.Current.Error("account", exception.Message);
        }
        finally { _isSyncing = false; SyncAddonsButton.IsEnabled = true; EndRequest(); }
    }

    private async void SyncAccountAddons(object sender, RoutedEventArgs e)
    {
        if (_isSyncing) return;
        var session = _accountClient.TryGetSession();
        if (session is null)
        {
            SetAccountStatus("Sign in first.", AccountStatus.Error);
            return;
        }

        BeginRequest("Syncing Stremio add-ons…");
        _isSyncing = true;
        SyncAddonsButton.IsEnabled = false;
        try
        {
            var user = await _accountClient.GetUserAsync(session.AuthKey, _request!.Token);
            AccountEmailBox.Text = user.Email;
            UpdateAccountNavigation(true, user.Email, user.Id, PrototypeSettings.GetProfileCache()?.AddonCount ?? 0, user.Avatar);
            await SyncAccountAddonsAsync(session.AuthKey, _request.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            SetAccountStatus("Sync failed: " + exception.Message, AccountStatus.Error);
            DiagnosticsService.Current.Error("account", exception.Message);
        }
        finally { _isSyncing = false; SyncAddonsButton.IsEnabled = true; EndRequest(); }
    }

    private async Task SyncAccountAddonsAsync(string authKey, CancellationToken cancellationToken)
    {
        var descriptors = await _accountClient.GetAddonCollectionAsync(authKey, cancellationToken);
        var streamAddons = new List<AddonEndpoint>();
        foreach (var descriptor in descriptors)
        {
            if (!Uri.TryCreate(descriptor.TransportUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                continue;

            var manifest = descriptor.Manifest ?? await _client.GetManifestAsync(uri, cancellationToken);
            var hasStreamResource = manifest.Resources.Any(resource =>
                resource.ValueKind == System.Text.Json.JsonValueKind.String && resource.GetString() == "stream" ||
                resource.ValueKind == System.Text.Json.JsonValueKind.Object &&
                resource.TryGetProperty("name", out var name) && name.GetString() == "stream");
            if (hasStreamResource) streamAddons.Add(new AddonEndpoint(manifest.Name, uri));
        }

        PrototypeSettings.SetStreamAddons(streamAddons);
        AddonUrlsBox.Text = PrototypeSettings.GetStreamAddonText();
        var profile = PrototypeSettings.GetProfileCache();
        PrototypeSettings.SaveProfileCache(new AccountProfileCache(AccountEmailBox.Text, profile?.Id ?? "", profile?.Avatar, streamAddons.Count));
        UpdateAccountNavigation(true, AccountEmailBox.Text, profile?.Id, streamAddons.Count);
        SetAccountStatus($"Connected. Synced {streamAddons.Count} stream add-on(s) from {descriptors.Count} account add-on(s).", AccountStatus.Success);
        DiagnosticsService.Current.Info("account", AccountStatusText.Text);
    }

    private async void SignOut(object sender, RoutedEventArgs e)
    {
        var confirmation = new ContentDialog
        {
            Title = "Sign out of Stremio?",
            Content = "You will need to sign in again to sync your account add-ons.",
            PrimaryButtonText = "Sign out",
            CloseButtonText = "Cancel"
        };
        if (await confirmation.ShowAsync() != ContentDialogResult.Primary) return;
        var session = _accountClient.TryGetSession();
        if (session is not null)
        {
            try { await _accountClient.LogoutAsync(session.AuthKey); }
            catch (Exception exception) { DiagnosticsService.Current.Warn("account", "Remote logout failed: " + exception.Message); }
        }
        AccountPasswordBox.Password = "";
        PrototypeSettings.ClearProfileCache();
        UpdateAccountNavigation(false);
        SetAccountStatus("Signed out.", AccountStatus.Neutral);
        SetSelectedNavigation(HomeButton);
        ShowPanel(HomePanel, "Home", "Movies and series from public Stremio catalogs");
    }

    private void ShowHome(object sender, RoutedEventArgs e)
    {
        SetSelectedNavigation(HomeButton);
        ShowPanel(HomePanel, "Home", "Movies and series from public Stremio catalogs");
    }

    private void ShowDiscover(object sender, RoutedEventArgs e)
    {
        SetSelectedNavigation(DiscoverButton);
        ShowPanel(DiscoverPanel, "Discover", "Search Cinemeta using the Stremio add-on protocol");
    }

    private void ShowLibrary(object sender, RoutedEventArgs e)
    {
        var items = PrototypeSettings.GetLibrary();
        LibraryGrid.ItemsSource = items;
        EmptyLibraryText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SetSelectedNavigation(LibraryNavButton);
        ShowPanel(LibraryPanel, "My library", "Titles saved locally on this Xbox prototype");
    }

    private void ShowPlaybackLab(object sender, RoutedEventArgs e)
    {
        SetSelectedNavigation(PlaybackLabButton);
        ShowPanel(PlaybackLabPanel, "Playback lab", "Test a direct stream through the native Xbox media pipeline");
    }

    private void ShowAddons(object sender, RoutedEventArgs e)
    {
        SetSelectedNavigation(AddonsButton);
        ShowPanel(AddonsPanel, "Stream add-ons", "Connect providers and an optional external Stremio Service");
    }

    private void ShowAccount(object sender, RoutedEventArgs e)
    {
        SetSelectedNavigation(AccountButton);
        ShowPanel(AccountPanel, _isSignedIn ? "Profile" : "Login",
            _isSignedIn ? "Manage your signed-in Stremio account" : "Sign in and synchronize your configured add-ons");
    }

    private void UpdateAccountNavigation(bool isSignedIn, string? email = null, string? id = null, int addonCount = 0, string? avatar = null)
    {
        _isSignedIn = isSignedIn;
        AccountNavLabel.Text = isSignedIn ? "Profile" : "Login";
        ProfileInitialBadge.Visibility = isSignedIn ? Visibility.Visible : Visibility.Collapsed;
        ProfileInitialText.Text = isSignedIn && !string.IsNullOrWhiteSpace(email) ? email[..1].ToUpperInvariant() : "";
        SignedOutAccountContent.Visibility = isSignedIn ? Visibility.Collapsed : Visibility.Visible;
        SignedInProfileContent.Visibility = isSignedIn ? Visibility.Visible : Visibility.Collapsed;
        ProfileEmailText.Text = isSignedIn && !string.IsNullOrWhiteSpace(email) ? email : "";
        ProfileIdText.Text = isSignedIn && !string.IsNullOrWhiteSpace(id) ? $"Account ID: {id}" : "Account ID: available after verification";
        ProfileAddonCountText.Text = isSignedIn ? $"Synced stream add-ons: {addonCount}" : "";
        if (isSignedIn && !string.IsNullOrWhiteSpace(email))
            PrototypeSettings.SaveProfileCache(new AccountProfileCache(email, id ?? "", avatar, addonCount));
    }

    private void SetAccountStatus(string message, AccountStatus status)
    {
        AccountStatusText.Text = message;
        var (background, foreground) = status switch
        {
            AccountStatus.Success => ("#1A3B2A", "#A7F3C5"),
            AccountStatus.Error => ("#4A1D28", "#FFC1C7"),
            _ => ("#202431", "#A6A8B6")
        };
        AccountStatusBanner.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255,
            Convert.ToByte(background.Substring(1, 2), 16), Convert.ToByte(background.Substring(3, 2), 16), Convert.ToByte(background.Substring(5, 2), 16)));
        AccountStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255,
            Convert.ToByte(foreground.Substring(1, 2), 16), Convert.ToByte(foreground.Substring(3, 2), 16), Convert.ToByte(foreground.Substring(5, 2), 16)));
    }

    private enum AccountStatus { Neutral, Success, Error }

    private void ShowDiagnostics(object sender, RoutedEventArgs e)
    {
        SetSelectedNavigation(DiagnosticsButton);
        ShowPanel(DiagnosticsPanel, "Diagnostics", "Bounded in-memory event log");
        UpdateMemory();
        _memoryTimer.Start();
    }

    private void SetSelectedNavigation(Button selected)
    {
        foreach (var button in new[]
                 {
                     HomeButton, DiscoverButton, LibraryNavButton, PlaybackLabButton,
                     AddonsButton, AccountButton, DiagnosticsButton
                 })
        {
            button.Style = (Style)Application.Current.Resources[
                ReferenceEquals(button, selected) ? "SelectedNavButtonStyle" : "NavButtonStyle"];
        }
    }

    private void ShowPanel(UIElement panel, string title, string subtitle)
    {
        _request?.Cancel();
        _memoryTimer.Stop();
        foreach (var candidate in new UIElement[]
                 { HomePanel, DiscoverPanel, LibraryPanel, PlaybackLabPanel, AddonsPanel, AccountPanel, DiagnosticsPanel })
        {
            candidate.Visibility = ReferenceEquals(candidate, panel) ? Visibility.Visible : Visibility.Collapsed;
        }
        PageTitle.Text = title;
        PageSubtitle.Text = subtitle;
    }

    private void BeginRequest(string status)
    {
        _request?.Cancel();
        _request = new CancellationTokenSource();
        BusyIndicator.IsActive = true;
        NetworkStatusText.Text = status;
    }

    private void EndRequest() => BusyIndicator.IsActive = false;

    private void UpdateMemory()
    {
        var used = MemoryManager.AppMemoryUsage / 1024d / 1024d;
        var limit = MemoryManager.AppMemoryUsageLimit / 1024d / 1024d;
        MemoryText.Text = $"Memory  {used:N1} MB / {limit:N1} MB  ({used / limit:P1})";
    }

    private void BackRequested(object? sender, BackRequestedEventArgs e)
    {
        if (Frame.CanGoBack)
        {
            e.Handled = true;
            Frame.GoBack();
        }
    }

    private static async Task ShowErrorAsync(string title, string message)
    {
        try
        {
            await new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "Back"
            }.ShowAsync();
        }
        catch (OperationCanceledException)
        {
            // The dialog was dismissed by navigation, suspension, or shutdown.
        }
    }
}

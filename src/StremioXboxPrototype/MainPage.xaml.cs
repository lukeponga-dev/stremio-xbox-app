using System.Collections.ObjectModel;
using StremioXboxPrototype.Models;
using StremioXboxPrototype.Services;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
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
        try
        {
            var session = await _accountClient.RestoreSessionAsync();
            if (session is not null)
            {
                AccountEmailBox.Text = session.Email;
                AccountStatusText.Text = $"Signed in as {session.Email}. Session verified by Stremio.";
            }
            else
            {
                AccountStatusText.Text = "Not signed in";
            }
        }
        catch (Exception exception)
        {
            AccountStatusText.Text = "Saved sign-in could not be verified. Check your connection, then try Sync add-ons.";
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
            AccountStatusText.Text = "Enter your Stremio email and password.";
            return;
        }

        BeginRequest("Signing in to Stremio…");
        try
        {
            var login = await _accountClient.LoginAsync(AccountEmailBox.Text, AccountPasswordBox.Password, _request!.Token);
            AccountPasswordBox.Password = "";
            AccountStatusText.Text = $"Signed in as {login.User?.Email ?? AccountEmailBox.Text}. Syncing add-ons…";
            await SyncAccountAddonsAsync(login.AuthKey, _request.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            AccountPasswordBox.Password = "";
            AccountStatusText.Text = "Sign-in failed: " + exception.Message;
            DiagnosticsService.Current.Error("account", exception.Message);
        }
        finally { EndRequest(); }
    }

    private async void SignInWithFacebook(object sender, RoutedEventArgs e)
    {
        BeginRequest("Starting Facebook sign-in…");
        try
        {
            var attempt = _accountClient.CreateFacebookLoginAttempt();
            AccountStatusText.Text = "Complete Facebook sign-in in Microsoft Edge, then return to this app.";
            if (!await Launcher.LaunchUriAsync(attempt.LoginUri))
                throw new InvalidOperationException("Xbox could not open the Stremio Facebook sign-in page.");

            var login = await _accountClient.CompleteFacebookLoginAsync(attempt.State, _request!.Token);
            AccountEmailBox.Text = login.User?.Email ?? "";
            AccountStatusText.Text = $"Signed in as {login.User?.Email ?? "Facebook user"}. Syncing add-ons…";
            await SyncAccountAddonsAsync(login.AuthKey, _request.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            AccountStatusText.Text = "Facebook sign-in failed: " + exception.Message;
            DiagnosticsService.Current.Error("account", exception.Message);
        }
        finally { EndRequest(); }
    }

    private async void SyncAccountAddons(object sender, RoutedEventArgs e)
    {
        var session = _accountClient.TryGetSession();
        if (session is null)
        {
            AccountStatusText.Text = "Sign in first.";
            return;
        }

        BeginRequest("Syncing Stremio add-ons…");
        try
        {
            var user = await _accountClient.GetUserAsync(session.AuthKey, _request!.Token);
            AccountEmailBox.Text = user.Email;
            await SyncAccountAddonsAsync(session.AuthKey, _request.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            AccountStatusText.Text = "Sync failed: " + exception.Message;
            DiagnosticsService.Current.Error("account", exception.Message);
        }
        finally { EndRequest(); }
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
        AccountStatusText.Text = $"Connected. Synced {streamAddons.Count} stream add-on(s) from {descriptors.Count} account add-on(s).";
        DiagnosticsService.Current.Info("account", AccountStatusText.Text);
    }

    private async void SignOut(object sender, RoutedEventArgs e)
    {
        var session = _accountClient.TryGetSession();
        if (session is not null)
        {
            try { await _accountClient.LogoutAsync(session.AuthKey); }
            catch (Exception exception) { DiagnosticsService.Current.Warn("account", "Remote logout failed: " + exception.Message); }
        }
        AccountPasswordBox.Password = "";
        AccountStatusText.Text = "Signed out.";
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
        ShowPanel(AccountPanel, "Stremio account", "Sign in and synchronize your configured add-ons");
    }

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

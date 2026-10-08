using System.Collections.ObjectModel;
using StremioXboxPrototype.Controls;
using StremioXboxPrototype.Models;
using StremioXboxPrototype.Services;
using Windows.System;
using Windows.Media.SpeechRecognition;
using Windows.UI.ViewManagement;
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
    private CancellationTokenSource? _serverRequest;
    private CancellationTokenSource? _posterRequest;
    private CancellationTokenSource? _voiceRequest;
    private PosterCard? _focusedCard;
    private readonly Dictionary<string, MetaItem> _metadataCache = new();
    private bool _startupCompleted;
    private Button? _firstKeyboardKey;
    private readonly DispatcherTimer _memoryTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private Control? _lastFocusedItem;
    private bool _isSignedIn;
    private bool _isSyncing;
    private bool _discoverHasResults;

    public ObservableCollection<DiagnosticEntry> Diagnostics => DiagnosticsService.Current.Entries;

    public MainPage()
    {
        InitializeComponent();
        AddonUrlsBox.Text = PrototypeSettings.GetStreamAddonText();
        UpdateAddonSummary();
        StreamingServiceUrlBox.Text = PrototypeSettings.GetStreamingServiceUrlText();
        PosterAnimationsToggle.IsOn = PrototypeSettings.GetPosterAnimationsEnabled();
        BuildSearchKeyboard();
        MovieSkeletons.ItemsSource = SeriesSkeletons.ItemsSource = Enumerable.Range(0, 8).ToList();
        _memoryTimer.Tick += (_, _) => UpdateMemory();
        SystemNavigationManager.GetForCurrentView().BackRequested += BackRequested;
        Loaded += PageLoaded;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        RefreshContinueWatching();
        if (_lastFocusedItem is not null)
        {
            _lastFocusedItem.Focus(FocusState.Programmatic);
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _request?.Cancel();
        _serverRequest?.Cancel();
        _posterRequest?.Cancel();
        _voiceRequest?.Cancel();
        base.OnNavigatedFrom(e);
    }

    private async void PageLoaded(object sender, RoutedEventArgs e)
    {
        if (_startupCompleted) return;
        _startupCompleted = true;
        HomeButton.Focus(FocusState.Programmatic);
        // Catalog and server checks are independent, so start both without making
        // the first screen wait for a sleeping hosted server.
        _ = LoadHomeAsync();
        _ = CheckSavedServerAsync();
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
    }

    private async Task LoadHomeAsync()
    {
        RefreshContinueWatching();
        BeginRequest("Loading public catalogs…");
        var request = _request!;
        HomeLoadingPanel.Visibility = ContinueWatchingGrid.Items.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        HomeShelves.Visibility = ContinueWatchingGrid.Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        HomeUnavailablePanel.Visibility = Visibility.Collapsed;
        try
        {
            // Load both shelves together. Each request is still isolated inside
            // StremioAddonClient, while this page owns cancellation and UI state.
            var movies = _client.GetCatalogAsync("movie", cancellationToken: request.Token);
            var series = _client.GetCatalogAsync("series", cancellationToken: request.Token);
            await Task.WhenAll(movies, series);
            request.Token.ThrowIfCancellationRequested();
            MovieGrid.ItemsSource = movies.Result.Take(20).ToList();
            SeriesGrid.ItemsSource = series.Result.Take(20).ToList();
            NetworkStatusText.Text = $"Loaded {movies.Result.Count + series.Result.Count} catalog items";
            DiagnosticsService.Current.Info("catalog", NetworkStatusText.Text);
            var empty = MovieGrid.Items.Count + SeriesGrid.Items.Count + ContinueWatchingGrid.Items.Count == 0;
            HomeShelves.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
            HomeUnavailablePanel.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            HomeStateTitle.Text = "No titles found yet";
            HomeStateText.Text = "Try refreshing the catalog in a moment.";
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            NetworkStatusText.Text = "Catalog unavailable";
            DiagnosticsService.Current.Error("catalog", exception.Message);
            HomeStateTitle.Text = "We couldn't load your movies and series";
            HomeStateText.Text = "Check your internet connection, then try again.";
            HomeUnavailablePanel.Visibility = Visibility.Visible;
            HomeShelves.Visibility = ContinueWatchingGrid.Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (ContinueWatchingGrid.Items.Count > 0)
            {
                HomeUnavailablePanel.Visibility = Visibility.Collapsed;
                PageSubtitle.Text = "Your saved progress is available. Use Refresh to retry popular titles.";
            }
        }
        finally
        {
            // A newer navigation or refresh may already own _request. Only the
            // request that is still current may hide the shared loading state.
            if (ReferenceEquals(_request, request))
            {
                HomeLoadingPanel.Visibility = Visibility.Collapsed;
                EndRequest();
            }
        }
    }

    private async void Search(object sender, RoutedEventArgs e) => await SearchAsync();

    private async void SearchBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            await SearchAsync();
        }
    }

    private void MainPageKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.GamepadY) return;
        e.Handled = true;
        if (DiscoverPanel.Visibility != Visibility.Visible)
            ShowDiscover(this, new RoutedEventArgs());
        SearchBox.Focus(FocusState.Programmatic);
    }

    private void SetDiscoverResultsVisible(bool visible)
    {
        _discoverHasResults = visible;
        DiscoverSuggestions.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
        DiscoverGrid.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task SearchAsync()
    {
        var query = SearchBox.Text.Trim();
        if (query.Length == 0)
        {
            DiscoverStatusText.Text = "Enter a movie or series title to search.";
            return;
        }
        SetSelectedNavigation(DiscoverButton);
        ShowPanel(DiscoverPanel, "Discover", "Find your next movie or series");
        SearchKeyboardPanel.Visibility = Visibility.Collapsed;
        SetDiscoverResultsVisible(true);
        InputPane.GetForCurrentView().TryHide();
        BeginRequest("Searching…");
        var request = _request!;
        DiscoverStatusText.Text = $"Searching for “{query}”…";
        DiscoverGrid.ItemsSource = null;
        try
        {
            var movies = _client.GetCatalogAsync("movie", search: query, cancellationToken: request.Token);
            var series = _client.GetCatalogAsync("series", search: query, cancellationToken: request.Token);
            await Task.WhenAll(movies, series);
            request.Token.ThrowIfCancellationRequested();
            DiscoverGrid.ItemsSource = movies.Result.Concat(series.Result).ToList();
            NetworkStatusText.Text = $"{DiscoverGrid.Items.Count} search results";
            DiscoverStatusText.Text = DiscoverGrid.Items.Count == 0
                ? $"No matches for “{query}”. Try another title or a shorter name."
                : $"Results for “{query}”";
            if (DiscoverGrid.Items.Count > 0) DiscoverGrid.Focus(FocusState.Programmatic);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            DiagnosticsService.Current.Error("search", exception.Message);
            DiscoverStatusText.Text = "Search couldn't connect. Check your internet connection and try again.";
        }
        finally
        {
            if (ReferenceEquals(_request, request)) EndRequest();
        }
    }

    private void BuildSearchKeyboard()
    {
        // Build the compact TV keyboard in code so every key receives identical
        // controller focus behavior without a large repeated XAML declaration.
        foreach (var keys in new[] { "1234567890", "QWERTYUIOP", "ASDFGHJKL", "ZXCVBNM" })
        {
            var row = new Grid();
            for (var index = 0; index < keys.Length; index++)
            {
                row.ColumnDefinitions.Add(new ColumnDefinition());
                var button = new Button
                {
                    Content = keys[index].ToString(), Tag = keys[index].ToString().ToLowerInvariant(),
                    Style = (Style)Application.Current.Resources["KeyboardKeyButtonStyle"],
                    HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(3, 0, 3, 0)
                };
                button.Click += SearchKeyboardKey;
                Grid.SetColumn(button, index);
                row.Children.Add(button);
                _firstKeyboardKey ??= button;
            }
            KeyboardRows.Children.Add(row);
        }
        var actions = new Grid();
        var labels = new[] { "Space", "Delete", "Clear", "Done" };
        for (var index = 0; index < labels.Length; index++)
        {
            actions.ColumnDefinitions.Add(new ColumnDefinition());
            var button = new Button
            {
                Content = labels[index], Tag = labels[index],
                Style = (Style)Application.Current.Resources["KeyboardKeyButtonStyle"],
                HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(3, 0, 3, 0)
            };
            button.Click += SearchKeyboardKey;
            Grid.SetColumn(button, index);
            actions.Children.Add(button);
        }
        KeyboardRows.Children.Add(actions);
    }

    private void ToggleSearchKeyboard(object sender, RoutedEventArgs e)
    {
        var opening = SearchKeyboardPanel.Visibility != Visibility.Visible;
        SearchKeyboardPanel.Visibility = opening ? Visibility.Visible : Visibility.Collapsed;
        DiscoverSuggestions.Visibility = opening || _discoverHasResults ? Visibility.Collapsed : Visibility.Visible;
        DiscoverGrid.Visibility = opening || !_discoverHasResults ? Visibility.Collapsed : Visibility.Visible;
        InputPane.GetForCurrentView().TryHide();
        if (opening) _firstKeyboardKey?.Focus(FocusState.Programmatic);
        else KeyboardButton.Focus(FocusState.Programmatic);
    }

    private async void SearchKeyboardKey(object sender, RoutedEventArgs e)
    {
        var key = (string)((Button)sender).Tag;
        var start = SearchBox.SelectionStart;
        var length = SearchBox.SelectionLength;
        var text = SearchBox.Text;
        if (key == "Done") { await SearchAsync(); return; }
        if (key == "Clear") { SearchBox.Text = ""; return; }
        if (key == "Delete")
        {
            if (length == 0 && start > 0) { start--; length = 1; }
            SearchBox.Text = text.Remove(start, length);
            SearchBox.Select(start, 0);
            return;
        }
        var inserted = key == "Space" ? " " : key;
        SearchBox.Text = text.Remove(start, length).Insert(start, inserted);
        SearchBox.Select(start + inserted.Length, 0);
    }

    private async void VoiceSearch(object sender, RoutedEventArgs e)
    {
        _voiceRequest?.Cancel();
        var request = new CancellationTokenSource();
        _voiceRequest = request;
        VoiceSearchButton.IsEnabled = false;
        SearchKeyboardPanel.Visibility = Visibility.Collapsed;
        SetDiscoverResultsVisible(_discoverHasResults);
        DiscoverStatusText.Text = "Say a movie or series title…";
        try
        {
            using var recognizer = new SpeechRecognizer();
            recognizer.Constraints.Add(new SpeechRecognitionTopicConstraint(SpeechRecognitionScenario.WebSearch, "titles"));
            recognizer.UIOptions.AudiblePrompt = "Say a movie or series title";
            recognizer.UIOptions.ExampleText = "For example, The Lord of the Rings";
            var compilation = await recognizer.CompileConstraintsAsync().AsTask(request.Token);
            if (compilation.Status != SpeechRecognitionResultStatus.Success)
                throw new InvalidOperationException("Speech recognition is unavailable.");
            var result = await recognizer.RecognizeWithUIAsync().AsTask(request.Token);
            request.Token.ThrowIfCancellationRequested();
            if (result.Status == SpeechRecognitionResultStatus.Success && !string.IsNullOrWhiteSpace(result.Text))
            {
                SearchBox.Text = result.Text;
                _voiceRequest = null;
                await SearchAsync();
            }
            else DiscoverStatusText.Text = "No title heard. Try Voice again or use Keyboard.";
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            DiagnosticsService.Current.Warn("voice-search", exception.Message);
            DiscoverStatusText.Text = "Voice search is unavailable. Check your microphone and speech permissions, or use Keyboard.";
        }
        finally
        {
            if (ReferenceEquals(_voiceRequest, request)) _voiceRequest = null;
            VoiceSearchButton.IsEnabled = true;
            request.Dispose();
        }
    }

    private void PosterContainerChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        var container = args.ItemContainer;
        container.GotFocus -= PosterGotFocus;
        container.LostFocus -= PosterLostFocus;
        if (args.InRecycleQueue)
        {
            FindPosterCard(container)?.SetFocused(false);
            return;
        }
        container.GotFocus += PosterGotFocus;
        container.LostFocus += PosterLostFocus;
        if (args.Item is MetaItem item)
            Windows.UI.Xaml.Automation.AutomationProperties.SetName(container, item.Name);
        if (args.Phase == 0) args.RegisterUpdateCallback(PosterContainerChanging);
        else if (sender == MovieGrid || sender == SeriesGrid || sender == ContinueWatchingGrid)
            FindPosterCard(container)?.SetCardWidth(TvLayout.GetShelfCardWidth(sender.ActualWidth));
        if (args.Phase > 0)
            FindPosterCard(container)?.SetFocused(container.FocusState != FocusState.Unfocused);
    }

    private void ShelfSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var shelf = (GridView)sender;
        for (var index = 0; index < shelf.Items.Count; index++)
            if (shelf.ContainerFromIndex(index) is DependencyObject container)
                FindPosterCard(container)?.SetCardWidth(TvLayout.GetShelfCardWidth(e.NewSize.Width));
    }

    private void ViewportSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var layout = TvLayout.GetViewportLayout(e.NewSize.Width, e.NewSize.Height);
        // Background surfaces occupy the full window. Only interactive content
        // receives TV-safe insets, recalculated whenever the window changes size.
        SidebarColumn.Width = new GridLength(layout.SidebarWidth);
        SidebarSurface.Padding = new Thickness(layout.HorizontalInset, layout.VerticalInset, 8, layout.VerticalInset);
        ContentSurface.Padding = new Thickness(layout.ContentGap, layout.VerticalInset,
            layout.HorizontalInset, layout.VerticalInset);
        HeaderActionsColumn.Width = layout.StackHeader ? new GridLength(0) : GridLength.Auto;
        Grid.SetColumn(HeaderActions, layout.StackHeader ? 0 : 1);
        Grid.SetRow(HeaderActions, layout.StackHeader ? 1 : 0);
        HeaderActions.Margin = layout.StackHeader ? new Thickness(0, 12, 0, 0) : new Thickness(0);
        HeaderText.Margin = layout.StackHeader ? new Thickness(0) : new Thickness(0, 0, 24, 0);
        PageSubtitle.Visibility = e.NewSize.Height < 600 ? Visibility.Collapsed : Visibility.Visible;
        PageTitle.FontSize = e.NewSize.Height < 600 ? 30 : 36;
        var contentWidth = e.NewSize.Width - layout.SidebarWidth - layout.ContentGap - layout.HorizontalInset;
        var compactSearch = contentWidth < 560;
        Grid.SetColumnSpan(SearchBox, compactSearch ? 4 : 1);
        foreach (var button in new[] { SearchSubmitButton, KeyboardButton, VoiceSearchButton })
        {
            Grid.SetRow(button, compactSearch ? 1 : 0);
            button.Margin = new Thickness(12, compactSearch ? 12 : 0, 0, 0);
        }
        DiscoverBrowseActions.Orientation = compactSearch ? Orientation.Vertical : Orientation.Horizontal;
        SearchKeyboardPanel.MaxHeight = Math.Max(80, Math.Min(220, e.NewSize.Height * 0.3));
    }

    private static PosterCard? FindPosterCard(DependencyObject root)
    {
        if (root is PosterCard card) return card;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var found = FindPosterCard(VisualTreeHelper.GetChild(root, index));
            if (found is not null) return found;
        }
        return null;
    }

    private async void PosterGotFocus(object sender, RoutedEventArgs e)
    {
        var card = FindPosterCard((DependencyObject)sender);
        if (card?.Item is not MetaItem item) return;
        _focusedCard?.SetFocused(false);
        _focusedCard = card;
        card.SetFocused(true);
        _posterRequest?.Cancel();
        var request = new CancellationTokenSource();
        _posterRequest = request;
        var key = $"{item.Type}/{item.Id}";
        try
        {
            if (!_metadataCache.TryGetValue(key, out var details))
            {
                if (!string.IsNullOrWhiteSpace(item.Runtime) && !string.IsNullOrWhiteSpace(item.ImdbRating)) details = item;
                else
                {
                    await Task.Delay(300, request.Token);
                    details = await _client.GetMetaAsync(item.Type, item.Id, request.Token) ?? item;
                }
                request.Token.ThrowIfCancellationRequested();
                if (_metadataCache.Count >= 100) _metadataCache.Clear();
                _metadataCache[key] = details;
            }
            if (card.IsPosterFocused && ReferenceEquals(card.Item, item))
            {
                card.SetDetails(details);
                Windows.UI.Xaml.Automation.AutomationProperties.SetHelpText((DependencyObject)sender, details.CardMetadata);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { DiagnosticsService.Current.Warn("poster", exception.Message); }
        finally
        {
            if (ReferenceEquals(_posterRequest, request)) _posterRequest = null;
            request.Dispose();
        }
    }

    private void PosterLostFocus(object sender, RoutedEventArgs e)
    {
        var card = FindPosterCard((DependencyObject)sender);
        card?.SetFocused(false);
        if (ReferenceEquals(_focusedCard, card))
        {
            _posterRequest?.Cancel();
            _focusedCard = null;
        }
    }

    private void PosterAnimationsChanged(object sender, RoutedEventArgs e)
    {
        PrototypeSettings.SetPosterAnimationsEnabled(((ToggleSwitch)sender).IsOn);
        _focusedCard?.SetFocused(true);
    }

    private async void RetryHome(object sender, RoutedEventArgs e) => await LoadHomeAsync();

    private void RefreshContinueWatching()
    {
        // Reuse the poster template and title navigation. The progress attachment
        // is UI-only (JsonIgnore), so it does not enter persisted catalog metadata.
        var items = PrototypeSettings.GetWatchHistory().Select(progress =>
        {
            progress.Item.WatchProgress = progress;
            return progress.Item;
        }).ToList();
        ContinueWatchingGrid.ItemsSource = items;
        ContinueWatchingShelf.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        // Local history remains available even when the public catalog is offline.
        if (items.Count > 0 && HomeLoadingPanel.Visibility != Visibility.Visible)
        {
            HomeShelves.Visibility = Visibility.Visible;
            HomeUnavailablePanel.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowSettings(object sender, RoutedEventArgs e)
    {
        SetSelectedNavigation(SettingsButton);
        ShowPanel(SettingsPanel, "Settings", "Make the TV experience comfortable for you");
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
        var input = AddonUrlsBox.Text.Trim();
        if (input.Length == 0)
        {
            _request?.Cancel();
            PrototypeSettings.SetStreamAddonText("");
            AddonSaveStatus.Text = "All configured add-ons removed.";
            UpdateAddonSummary();
            return;
        }
        var candidates = PrototypeSettings.ParseStreamAddons(input);
        if (candidates.Count == 0)
        {
            AddonSaveStatus.Text = "Enter at least one HTTPS manifest URL.";
            return;
        }

        BeginRequest("Validating add-on manifests…");
        var request = _request!;
        var accepted = new List<AddonEndpoint>();
        try
        {
            foreach (var candidate in candidates)
            {
                try
                {
                    var manifest = await _client.GetManifestAsync(candidate.ManifestUri, request.Token);
                    request.Token.ThrowIfCancellationRequested();
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

            request.Token.ThrowIfCancellationRequested();
            if (accepted.Count == 0)
            {
                AddonSaveStatus.Text = "No valid stream add-ons found. Your saved add-ons have been kept. See Diagnostics.";
                return;
            }
            PrototypeSettings.SetStreamAddons(accepted);
            AddonUrlsBox.Text = PrototypeSettings.GetStreamAddonText();
            AddonSaveStatus.Text = $"Saved {accepted.Count}: {string.Join(", ", accepted.Select(addon => addon.Name))}.";
            UpdateAddonSummary();
            DiagnosticsService.Current.Info("settings", $"Validated and saved {accepted.Count} stream add-ons");
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (ReferenceEquals(_request, request)) EndRequest();
        }
    }

    private async void SaveStreamingService(object sender, RoutedEventArgs e)
    {
        if (!Uri.TryCreate(StreamingServiceUrlBox.Text.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            StreamingServiceStatus.Text = "Enter an absolute HTTP or HTTPS Stremio Service URL.";
            return;
        }

        await ConnectServerAsync(uri, save: true);
    }

    private async Task CheckSavedServerAsync()
    {
        var uri = PrototypeSettings.GetStreamingServiceUrl();
        if (uri is null)
        {
            ServerConnectionState.Text = "Server: disconnected";
            StreamingServiceStatus.Text = "Enter your server address to connect.";
            HomeConnectionText.Text = "Playback server not connected";
            HomeReconnectButton.Content = "Connect";
            HomeConnectionNotice.Visibility = Visibility.Visible;
            return;
        }
        await ConnectServerAsync(uri, save: false);
    }

    private async Task ConnectServerAsync(Uri uri, bool save)
    {
        // A new attempt supersedes the previous one. This prevents a slow server
        // response from overwriting the status of a more recent connection.
        _serverRequest?.Cancel();
        var request = new CancellationTokenSource();
        _serverRequest = request;
        ConnectServerButton.IsEnabled = false;
        HomeReconnectButton.IsEnabled = false;
        HomeConnectionText.Text = "Connecting to playback server…";
        ServerConnectionState.Text = "Server: checking…";
        StreamingServiceStatus.Text = $"Connecting to {uri.Host}:{uri.Port}…";
        try
        {
            var version = await _streamingServiceClient.TestAsync(uri, request.Token);
            request.Token.ThrowIfCancellationRequested();
            // Persist a user-entered address only after it proves that it exposes
            // the Stremio Service settings endpoint.
            if (save) PrototypeSettings.SetStreamingServiceUrl(uri);
            StreamingServiceUrlBox.Text = PrototypeSettings.GetStreamingServiceUrlText();
            ServerConnectionState.Text = "Server: connected";
            HomeConnectionNotice.Visibility = Visibility.Collapsed;
            StreamingServiceStatus.Text = $"Connected to {uri.Host}:{uri.Port} · Stremio {version}. Ready to stream.";
            DiagnosticsService.Current.Info("streaming-service", $"Connected to {uri.Host}");
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception exception)
        {
            ServerConnectionState.Text = "Server: unavailable";
            HomeConnectionText.Text = "Playback server offline";
            HomeReconnectButton.Content = "Reconnect";
            HomeConnectionNotice.Visibility = Visibility.Visible;
            StreamingServiceStatus.Text = $"Cannot reach {uri.Host}:{uri.Port}. The hosted server may still be waking up; wait a minute and try again. " + exception.Message;
            DiagnosticsService.Current.Error("streaming-service", exception.Message);
        }
        finally
        {
            if (ReferenceEquals(_serverRequest, request))
            {
                _serverRequest = null;
                ConnectServerButton.IsEnabled = true;
                HomeReconnectButton.IsEnabled = true;
            }
            request.Dispose();
        }
    }

    private void DisconnectServer(object sender, RoutedEventArgs e)
    {
        _serverRequest?.Cancel();
        PrototypeSettings.ClearStreamingServiceUrl();
        StreamingServiceUrlBox.Text = "";
        ServerConnectionState.Text = "Server: disconnected";
        HomeConnectionText.Text = "Playback server not connected";
        HomeReconnectButton.Content = "Connect";
        HomeConnectionNotice.Visibility = Visibility.Visible;
        StreamingServiceStatus.Text = "Disconnected. Enter a server address to reconnect.";
    }

    private void ShowServer(object sender, RoutedEventArgs e)
    {
        SetSelectedNavigation(ServerButton);
        ShowPanel(ServerPanel, "Stremio server", "Connect your TV to your streaming server");
        StreamingServiceUrlBox.Focus(FocusState.Programmatic);
    }

    private async void ShowConnectionDetails(object sender, RoutedEventArgs e)
    {
        await ShowErrorAsync("Playback server", PrototypeSettings.GetStreamingServiceUrl() is null
            ? "Connect a playback server when you're ready to watch. You can still browse movies, series, and your library. Choose Connect to enter your server address."
            : "Your playback server is offline. You can still browse movies, series, and your library. Check that your server is running, then choose Reconnect.");
    }

    private async void ReconnectServer(object sender, RoutedEventArgs e)
    {
        var uri = PrototypeSettings.GetStreamingServiceUrl();
        if (uri is null) { ShowServer(sender, e); return; }
        await ConnectServerAsync(uri, save: false);
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
        UpdateAddonSummary();
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
        ShowPanel(HomePanel, "Home", "Pick up where you left off or find your next favourite");
        if (MovieGrid.Items.Count + SeriesGrid.Items.Count == 0) await LoadHomeAsync();
    }

    private async void ShowHome(object sender, RoutedEventArgs e)
    {
        RefreshContinueWatching();
        SetSelectedNavigation(HomeButton);
        ShowPanel(HomePanel, "Home", "Pick up where you left off or find your next favourite");
        if (MovieGrid.Items.Count + SeriesGrid.Items.Count == 0) await LoadHomeAsync();
    }

    private void ShowDiscover(object sender, RoutedEventArgs e)
    {
        SetSelectedNavigation(DiscoverButton);
        ShowPanel(DiscoverPanel, "Discover", "Find your next movie or series");
        SearchBox.Text = "";
        DiscoverStatusText.Text = "Search for a title or browse a popular collection.";
        SetDiscoverResultsVisible(false);
        SearchBox.Focus(FocusState.Programmatic);
    }

    private async void ShowMovieCatalog(object sender, RoutedEventArgs e) => await ShowCatalogAsync("movie");
    private async void ShowSeriesCatalog(object sender, RoutedEventArgs e) => await ShowCatalogAsync("series");

    private async Task ShowCatalogAsync(string type)
    {
        ShowDiscover(this, new RoutedEventArgs());
        SearchKeyboardPanel.Visibility = Visibility.Collapsed;
        SetDiscoverResultsVisible(true);
        SearchBox.Text = "";
        var label = type == "movie" ? "Popular movies" : "Popular series";
        BeginRequest("Loading " + label.ToLowerInvariant());
        var request = _request!;
        DiscoverGrid.ItemsSource = null;
        DiscoverStatusText.Text = "Loading " + label.ToLowerInvariant() + "…";
        try
        {
            var items = await _client.GetCatalogAsync(type, cancellationToken: request.Token);
            request.Token.ThrowIfCancellationRequested();
            DiscoverGrid.ItemsSource = items;
            DiscoverStatusText.Text = items.Count == 0 ? "No titles yet. Try again in a moment." : label;
            if (items.Count > 0) DiscoverGrid.Focus(FocusState.Programmatic);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            DiagnosticsService.Current.Error("catalog", exception.Message);
            DiscoverStatusText.Text = "We couldn't load these titles. Check your internet connection and try again.";
        }
        finally { if (ReferenceEquals(_request, request)) EndRequest(); }
    }

    private void ShowLibrary(object sender, RoutedEventArgs e)
    {
        var items = PrototypeSettings.GetLibrary();
        LibraryGrid.ItemsSource = items;
        EmptyLibraryText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SetSelectedNavigation(LibraryNavButton);
        ShowPanel(LibraryPanel, "My library", "Titles saved locally on this Xbox");
    }

    private void ShowPlaybackLab(object sender, RoutedEventArgs e)
    {
        SetSelectedNavigation(PlaybackLabButton);
        ShowPanel(PlaybackLabPanel, "Playback lab", "Test a direct stream through the native Xbox media pipeline");
        DirectUrlBox.Focus(FocusState.Programmatic);
    }

    private void ShowAddons(object sender, RoutedEventArgs e)
    {
        SetSelectedNavigation(AddonsButton);
        UpdateAddonSummary();
        ShowPanel(AddonsPanel, "Stream add-ons", "Add providers or sync them from your Stremio account");
    }

    private void UpdateAddonSummary()
    {
        var addons = PrototypeSettings.GetStreamAddons();
        AddonCountText.Text = addons.Count == 1 ? "1 connected" : $"{addons.Count} connected";
        AddonSummaryText.Text = addons.Count == 0
            ? "No providers connected yet"
            : string.Join(" • ", addons.Select(addon => addon.Name));
    }

    private void ShowAccount(object sender, RoutedEventArgs e)
    {
        SetSelectedNavigation(SettingsButton);
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
        SettingsButton.Focus(FocusState.Programmatic);
        UpdateMemory();
        _memoryTimer.Start();
    }

    private void SetSelectedNavigation(Button selected)
    {
        // Advanced tools belong to Settings, which stays selected in the rail.
        if (selected == PlaybackLabButton || selected == ServerButton || selected == DiagnosticsButton)
            selected = SettingsButton;
        foreach (var button in new[] { HomeButton, DiscoverButton, LibraryNavButton, AddonsButton, SettingsButton })
        {
            button.Style = (Style)Application.Current.Resources[ReferenceEquals(button, selected)
                ? "SelectedNavButtonStyle" : "NavButtonStyle"];
        }
    }

    private void ShowPanel(UIElement panel, string title, string subtitle)
    {
        var focusedElement = FocusManager.GetFocusedElement() as DependencyObject;
        var focusWasInContent = false;
        for (var ancestor = focusedElement; ancestor is not null; ancestor = VisualTreeHelper.GetParent(ancestor))
        {
            if (ReferenceEquals(ancestor, ContentSurface)) { focusWasInContent = true; break; }
        }
        // Requests, animations, speech, and diagnostic polling belong to the
        // visible panel. Stop them before transferring focus to another panel.
        _request?.Cancel();
        _memoryTimer.Stop();
        BusyIndicator.IsActive = false;
        _posterRequest?.Cancel();
        _focusedCard?.SetFocused(false);
        _focusedCard = null;
        if (!ReferenceEquals(panel, DiscoverPanel))
        {
            _voiceRequest?.Cancel();
            SearchKeyboardPanel.Visibility = Visibility.Collapsed;
            SetDiscoverResultsVisible(_discoverHasResults);
            InputPane.GetForCurrentView().TryHide();
        }
        foreach (var candidate in new UIElement[]
                 { HomePanel, DiscoverPanel, LibraryPanel, PlaybackLabPanel, AddonsPanel, ServerPanel, AccountPanel, SettingsPanel, DiagnosticsPanel })
        {
            candidate.Visibility = ReferenceEquals(candidate, panel) ? Visibility.Visible : Visibility.Collapsed;
        }
        PageTitle.Text = title;
        PageSubtitle.Text = subtitle;
        HeaderSearchButton.Visibility = ReferenceEquals(panel, DiscoverPanel) ? Visibility.Collapsed : Visibility.Visible;
        HomeRefreshButton.Visibility = ReferenceEquals(panel, HomePanel) ? Visibility.Visible : Visibility.Collapsed;
        if (focusWasInContent && FocusManager.FindFirstFocusableElement(panel) is Control firstControl)
            firstControl.Focus(FocusState.Programmatic);
    }

    private void BeginRequest(string status)
    {
        // One foreground catalog/account operation owns the shared progress UI.
        // Cancelling here also prevents stale results appearing after navigation.
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
        if (!ReferenceEquals(Frame.Content, this)) return;
        if (SearchKeyboardPanel.Visibility == Visibility.Visible)
        {
            e.Handled = true;
            SearchKeyboardPanel.Visibility = Visibility.Collapsed;
            SetDiscoverResultsVisible(_discoverHasResults);
            KeyboardButton.Focus(FocusState.Programmatic);
            return;
        }
        if (ServerPanel.Visibility == Visibility.Visible || PlaybackLabPanel.Visibility == Visibility.Visible ||
            DiagnosticsPanel.Visibility == Visibility.Visible)
        {
            var returnTarget = ServerPanel.Visibility == Visibility.Visible ? ServerButton :
                PlaybackLabPanel.Visibility == Visibility.Visible ? PlaybackLabButton : DiagnosticsButton;
            e.Handled = true;
            ShowSettings(this, new RoutedEventArgs());
            returnTarget.Focus(FocusState.Programmatic);
        }
        else if (Frame.CanGoBack)
        {
            e.Handled = true;
            Frame.GoBack();
        }
        else if (HomePanel.Visibility != Visibility.Visible)
        {
            e.Handled = true;
            ShowHome(this, new RoutedEventArgs());
            HomeButton.Focus(FocusState.Programmatic);
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

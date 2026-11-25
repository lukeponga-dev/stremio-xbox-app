using StremioXboxPrototype.Models;
using StremioXboxPrototype.Services;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

namespace StremioXboxPrototype;

/// <summary>Shows metadata, watchlist actions, and streams returned by the configured providers.</summary>
/// <remarks>Metadata enrichment and provider lookups have separate cancellation lifetimes.</remarks>
public sealed partial class DetailsPage : Page
{
    private readonly StremioAddonClient _client = new();
    private MetaItem? _item;
    private CancellationTokenSource? _request;
    private CancellationTokenSource? _metadataRequest;
    private bool _streamsLoaded;
    private bool _isOpeningStream;
    private bool _hasReceivedInitialFocus;
    private WatchProgress? _resume;

    public DetailsPage()
    {
        InitializeComponent();
        Loaded += DetailsPageLoaded;
    }

    private void DetailsPageLoaded(object sender, RoutedEventArgs e)
    {
        if (_hasReceivedInitialFocus) return;
        _hasReceivedInitialFocus = true;
        LoadStreamsButton.Focus(FocusState.Programmatic);
    }

    private void DetailsPageKeyDown(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key is Windows.System.VirtualKey.GamepadB or Windows.System.VirtualKey.Escape)
        {
            if (Frame.CanGoBack) Frame.GoBack();
            e.Handled = true;
        }
    }

    private void ViewportSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var width = Math.Max(1, e.NewSize.Width);
        var height = Math.Max(1, e.NewSize.Height);
        var compact = width < 1180 || height < 680;
        var stacked = width < 1100;

        DetailsContent.Padding = new Thickness(
            Math.Clamp(width * 0.035, 18, 48),
            Math.Clamp(height * 0.035, 12, 24),
            Math.Clamp(width * 0.035, 18, 48),
            Math.Clamp(height * 0.025, 10, 20));
        // Let the synopsis/actions measure to their content. A fixed hero row
        // clipped the description and buttons on shorter Xbox/desktop windows.
        DetailsContent.RowDefinitions[1].Height = GridLength.Auto;
        DetailsContent.RowDefinitions[2].Height = new GridLength(1, GridUnitType.Star);

        DetailsSections.ColumnDefinitions[0].Width = stacked ? new GridLength(1, GridUnitType.Star) : new GridLength(1.05, GridUnitType.Star);
        DetailsSections.ColumnDefinitions[1].Width = stacked ? new GridLength(0) : new GridLength(0.95, GridUnitType.Star);
        Grid.SetColumn(StreamsPanel, stacked ? 0 : 1);
        Grid.SetRow(StreamsPanel, stacked ? 1 : 0);
        DetailsSections.RowDefinitions[1].Height = stacked ? GridLength.Auto : new GridLength(0);
        DetailsSections.RowDefinitions[0].Height = GridLength.Auto;

        BreadcrumbText.Visibility = width < 760 ? Visibility.Collapsed : Visibility.Visible;
        TitleText.FontSize = Math.Clamp(width * 0.045, 28, compact ? 40 : 54);
        DescriptionText.FontSize = width < 760 ? 15 : 17;
        EpisodePicker.Width = Math.Min(420, Math.Max(220, width * 0.48));

        // Keep the primary actions and stream list connected as the layout stacks.
        LoadStreamsButton.XYFocusRight = LibraryButton;
        LibraryButton.XYFocusLeft = LoadStreamsButton;
        var streamEntry = EpisodePicker.Visibility == Visibility.Visible ? (Control)EpisodePicker : StreamList;
        LoadStreamsButton.XYFocusDown = streamEntry;
        LibraryButton.XYFocusDown = streamEntry;
        EpisodePicker.XYFocusUp = LoadStreamsButton;
        EpisodePicker.XYFocusDown = StreamList;
        StreamList.XYFocusUp = EpisodePicker.Visibility == Visibility.Visible ? EpisodePicker : LoadStreamsButton;
        BackButton.XYFocusDown = LoadStreamsButton;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ResetStreamOpening();

        // A page instance can be revisited while an earlier metadata request is
        // still unwinding. Cancel it and give this navigation its own lifetime.
        _metadataRequest?.Cancel();
        var metadataRequest = new CancellationTokenSource();
        _metadataRequest = metadataRequest;

        _item = e.Parameter as MetaItem;
        if (_item is null)
        {
            metadataRequest.Dispose();
            _metadataRequest = null;
            return;
        }

        _resume = _item.WatchProgress;
        ApplyItem(_item);
        UpdateLibraryButton();
        try
        {
            try
            {
                var complete = await _client.GetMetaAsync(_item.Type, _item.Id, metadataRequest.Token);
                metadataRequest.Token.ThrowIfCancellationRequested();
                if (complete is not null)
                {
                    _item = complete;
                    ApplyItem(complete);
                }
            }
            catch (OperationCanceledException) when (metadataRequest.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                DiagnosticsService.Current.Warn("details", "Metadata enrichment failed: " + exception.Message);
            }

            metadataRequest.Token.ThrowIfCancellationRequested();
            await LoadStreamsAsync();
        }
        catch (OperationCanceledException) when (metadataRequest.IsCancellationRequested)
        {
            // Leaving Details cancels both metadata enrichment and stream lookup.
        }
        finally
        {
            if (ReferenceEquals(_metadataRequest, metadataRequest))
                _metadataRequest = null;
            metadataRequest.Dispose();
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _metadataRequest?.Cancel();
        _request?.Cancel();
        base.OnNavigatedFrom(e);
    }

    private void ApplyItem(MetaItem item)
    {
        TitleText.Text = item.Name;
        SubtitleText.Text = item.CardMetadata;
        var description = item.Description ?? "No description supplied by the metadata add-on.";
        DescriptionText.Text = description;
        AboutDescriptionText.Text = description;
        AboutMetadataText.Text = string.Join(Environment.NewLine, new[]
        {
            $"Type: {item.Type}",
            $"Release: {item.ReleaseInfo ?? "Unknown"}",
            $"Rating: {(string.IsNullOrWhiteSpace(item.ImdbRating) ? "Not rated" : item.ImdbRating)}",
            $"Runtime: {item.Runtime ?? "Not supplied"}",
            $"Episodes: {(item.Videos.Count > 0 ? item.Videos.Count.ToString() : "Not supplied")}"
        });
        if (Uri.TryCreate(item.Background, UriKind.Absolute, out var background)) Backdrop.Source = new BitmapImage(background);

        EpisodePicker.Visibility = item.Videos.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        EpisodePicker.ItemsSource = item.Videos;
        if (item.Videos.Count > 0)
            EpisodePicker.SelectedItem = item.Videos.FirstOrDefault(video => video.Id == _resume?.VideoId) ?? item.Videos[0];

        // Episode availability is data-driven, so refresh the directional links
        // after metadata arrives rather than pointing D-pad focus at a hidden control.
        var streamEntry = EpisodePicker.Visibility == Visibility.Visible ? (Control)EpisodePicker : StreamList;
        LoadStreamsButton.XYFocusDown = streamEntry;
        LibraryButton.XYFocusDown = streamEntry;
        StreamList.XYFocusUp = EpisodePicker.Visibility == Visibility.Visible ? EpisodePicker : LoadStreamsButton;
    }

    private void GoBack(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack) Frame.GoBack();
    }

    private async void PlayFirstStream(object sender, RoutedEventArgs e)
    {
        if (StreamList.Items.Count > 0 && StreamList.Items[0] is StreamItem first)
        {
            await PlaySelectedStreamAsync(first);
            return;
        }
        await LoadStreamsAsync();
        if (StreamList.Items.Count > 0 && StreamList.Items[0] is StreamItem available)
            await PlaySelectedStreamAsync(available);
    }

    private void ToggleLibrary(object sender, RoutedEventArgs e)
    {
        if (_item is null) return;
        var added = PrototypeSettings.ToggleLibrary(_item);
        DiagnosticsService.Current.Info("library", $"{(added ? "Added" : "Removed")} {_item.Id}");
        UpdateLibraryButton();
    }

    private void UpdateLibraryButton()
    {
        if (_item is not null)
            LibraryButton.Content = PrototypeSettings.IsInLibrary(_item.Id) ? "Remove from library" : "Add to library";
    }

    private async void LoadStreams(object sender, RoutedEventArgs e) => await LoadStreamsAsync();
    private async void EpisodeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_streamsLoaded) await LoadStreamsAsync();
    }

    private async Task LoadStreamsAsync()
    {
        if (_item is null) return;
        var addons = PrototypeSettings.GetStreamAddons();
        if (addons.Count == 0)
        {
            StreamStatusText.Text = "Add a compatible provider from Add-ons, or sign in to sync your configured providers.";
            StreamList.ItemsSource = null;
            _streamsLoaded = false;
            return;
        }

        _request?.Cancel();
        var request = new CancellationTokenSource();
        _request = request;
        var cancellationToken = request.Token;
        BusyIndicator.IsActive = true;
        LoadStreamsButton.IsEnabled = false;
        StreamStatusText.Text = $"Checking {addons.Count} provider(s) for playable streams...";
        StreamList.ItemsSource = null;
        var videoId = (EpisodePicker.SelectedItem as VideoItem)?.Id ?? _resume?.VideoId ?? _item.Id;

        var requests = addons.Select(addon => GetStreamsFromAddonAsync(addon, _item.Type, videoId, cancellationToken)).ToList();

        var streams = new List<StreamItem>();
        try
        {
            while (requests.Count > 0)
            {
                var completed = await Task.WhenAny(requests);
                requests.Remove(completed);
                cancellationToken.ThrowIfCancellationRequested();
                streams.AddRange((await completed).Where(IsPlayable));
                cancellationToken.ThrowIfCancellationRequested();

                var ordered = OrderAndDeduplicateStreams(streams);
                StreamList.ItemsSource = ordered;
                StreamStatusText.Text = DescribeStreams(ordered, requests.Count > 0);
            }

            var playable = OrderAndDeduplicateStreams(streams);
            StreamList.ItemsSource = playable;
            _streamsLoaded = true;
            StreamStatusText.Text = playable.Count == 0
                ? "No playable streams found. Try another episode or check your providers in Add-ons."
                : DescribeStreams(playable, checkingProviders: false) +
                  (PrototypeSettings.GetWatchProgress(_item.Type, _item.Id, videoId) is WatchProgress progress
                      ? $" Choose a source to resume | {progress.RemainingText}."
                      : "");
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (ReferenceEquals(_request, request))
            {
                BusyIndicator.IsActive = false;
                LoadStreamsButton.IsEnabled = true;
                _request = null;
            }
            request.Dispose();
        }
    }

    private static bool IsPlayable(StreamItem stream) =>
        stream.Resolution.Kind is StreamResolutionKind.NativeDirect or StreamResolutionKind.RequiresStreamingService;

    private static string DescribeStreams(IReadOnlyCollection<StreamItem> streams, bool checkingProviders)
    {
        var direct = streams.Count(stream => stream.Resolution.Kind == StreamResolutionKind.NativeDirect);
        var service = streams.Count(stream => stream.Resolution.Kind == StreamResolutionKind.RequiresStreamingService);
        var parts = new List<string>();
        if (direct > 0) parts.Add($"{direct} direct");
        if (service > 0) parts.Add($"{service} torrent/server-backed");
        if (parts.Count == 0) return checkingProviders ? "Checking providers..." : "No playable streams found.";

        var status = string.Join(" + ", parts) + " stream(s) found";
        if (service > 0 && PrototypeSettings.GetStreamingServiceUrl() is null)
            status += ". Connect a compatible streaming service to use torrent sources.";
        if (checkingProviders) status += " Checking more providers...";
        return status;
    }

    private static List<StreamItem> OrderAndDeduplicateStreams(IEnumerable<StreamItem> streams) => StreamPlaybackOrder.Order(streams)
        .GroupBy(StreamIdentity, StringComparer.OrdinalIgnoreCase)
        .Select(group => group.First())
        .ToList();

    private static string StreamIdentity(StreamItem stream)
    {
        if (stream.Resolution.PlaybackUri is not null) return stream.Resolution.PlaybackUri.AbsoluteUri;
        if (!string.IsNullOrWhiteSpace(stream.InfoHash)) return $"torrent:{stream.InfoHash}:{stream.FileIndex}";
        if (!string.IsNullOrWhiteSpace(stream.Url)) return stream.Url;
        return $"{stream.Provider}:{stream.Name}:{stream.Title}:{stream.Description}";
    }

    private async Task<IReadOnlyList<StreamItem>> GetStreamsFromAddonAsync(AddonEndpoint addon, string type,
        string videoId, CancellationToken cancellationToken)
    {
        try
        {
            return await _client.GetStreamsAsync(addon, type, videoId, cancellationToken);
        }
        catch (OperationCanceledException) { return Array.Empty<StreamItem>(); }
        catch (Exception exception)
        {
            DiagnosticsService.Current.Warn("streams", $"{addon.Name}: {exception.Message}");
            return Array.Empty<StreamItem>();
        }
    }

    private void StreamContainerChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue || args.ItemIndex != 0) return;

        // Only the first stream returns to the hero controls. Later rows retain
        // the ListView's natural up/down D-pad sequence.
        args.ItemContainer.XYFocusUp = EpisodePicker.Visibility == Visibility.Visible
            ? EpisodePicker
            : LoadStreamsButton;
    }

    private async void PlayStream(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is StreamItem stream)
            await PlaySelectedStreamAsync(stream);
    }

    private async Task PlaySelectedStreamAsync(StreamItem stream)
    {
        if (_item is null || _isOpeningStream) return;
        DiagnosticsService.Current.Info("streams", $"Selected source from {stream.Provider}: {stream.Resolution.Kind}");
        _isOpeningStream = true;
        StreamList.IsEnabled = false;
        LoadStreamsButton.IsEnabled = false;
        StreamStatusText.Text = stream.Resolution.Kind == StreamResolutionKind.NativeDirect
            ? "Opening stream..."
            : "Preparing stream through Stremio Service...";

        if (stream.Resolution.Kind == StreamResolutionKind.NativeDirect && stream.Resolution.PlaybackUri is not null)
        {
            OpenPlayer(new PlaybackRequest(stream.Resolution.PlaybackUri, _item.Name, stream.Provider,
                _item, (EpisodePicker.SelectedItem as VideoItem)?.Id ?? _resume?.VideoId ?? _item.Id));
            return;
        }

        if (stream.Resolution.Kind == StreamResolutionKind.RequiresStreamingService)
        {
            var serviceUrl = PrototypeSettings.GetStreamingServiceUrl();
            if (serviceUrl is null)
            {
                await new ContentDialog
                {
                    Title = "Connect your playback server",
                    Content = "Open Server under Developer / Advanced, connect your server, then try this stream again.",
                    CloseButtonText = "OK"
                }.ShowAsync();
                ResetStreamOpening();
                return;
            }

            OpenPlayer(new StreamPlaybackRequest(stream, serviceUrl, _item.Name,
                _item, (EpisodePicker.SelectedItem as VideoItem)?.Id ?? _resume?.VideoId ?? _item.Id));
            return;
        }

        await new ContentDialog
        {
            Title = "Stream is not native-direct",
            Content = stream.Resolution.Label + ". This source cannot be played directly on Xbox. Choose a native stream or configure a compatible streaming service, then try again.",
            CloseButtonText = "Choose another stream"
        }.ShowAsync();
        ResetStreamOpening();
    }

    private void OpenPlayer(object request)
    {
        try
        {
            if (Frame.Navigate(typeof(PlayerPage), request)) return;
            throw new InvalidOperationException("The player page could not be opened.");
        }
        catch (Exception exception)
        {
            ResetStreamOpening();
            StreamStatusText.Text = "Could not open player: " + exception.Message;
            DiagnosticsService.Current.Error("player-navigation", exception.ToString());
        }
    }

    private void ResetStreamOpening()
    {
        _isOpeningStream = false;
        StreamList.IsEnabled = true;
        LoadStreamsButton.IsEnabled = true;
    }
}

using StremioXboxPrototype.Models;
using StremioXboxPrototype.Services;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

namespace StremioXboxPrototype;

public sealed partial class DetailsPage : Page
{
    private readonly StremioAddonClient _client = new();
    private MetaItem? _item;
    private CancellationTokenSource? _request;
    private bool _streamsLoaded;
    private bool _isOpeningStream;
    private WatchProgress? _resume;

    public DetailsPage() => InitializeComponent();

    private void ViewportSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var compact = e.NewSize.Width < 1180 || e.NewSize.Height < 650;
        DetailsContent.Padding = compact ? new Thickness(28, 16, 28, 16) : new Thickness(48, 22, 48, 22);
        DetailsContent.RowDefinitions[1].Height = new GridLength(compact ? 250 : 300);
        DetailsContent.RowDefinitions[2].Height = new GridLength(1, GridUnitType.Star);
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ResetStreamOpening();
        _item = e.Parameter as MetaItem;
        if (_item is null) return;
        // Metadata enrichment replaces the compact shelf item, so keep the saved
        // episode identity separately before fetching the full title details.
        _resume = _item.WatchProgress;
        ApplyItem(_item);
        UpdateLibraryButton();
        LibraryButton.Focus(FocusState.Programmatic);

        try
        {
            var complete = await _client.GetMetaAsync(_item.Type, _item.Id);
            if (complete is not null)
            {
                _item = complete;
                ApplyItem(complete);
            }
        }
        catch (Exception exception)
        {
            DiagnosticsService.Current.Warn("details", "Metadata enrichment failed: " + exception.Message);
        }

        await LoadStreamsAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
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

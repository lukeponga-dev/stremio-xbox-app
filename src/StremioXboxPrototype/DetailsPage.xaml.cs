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
    private readonly StremioStreamingServiceClient _streamingServiceClient = new();
    private MetaItem? _item;
    private CancellationTokenSource? _request;
    private bool _streamsLoaded;

    public DetailsPage() => InitializeComponent();

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _item = e.Parameter as MetaItem;
        if (_item is null) return;
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
        SubtitleText.Text = item.Subtitle;
        DescriptionText.Text = item.Description ?? "No description supplied by the metadata add-on.";
        if (Uri.TryCreate(item.Poster, UriKind.Absolute, out var poster)) Poster.Source = new BitmapImage(poster);
        if (Uri.TryCreate(item.Background, UriKind.Absolute, out var background)) Backdrop.Source = new BitmapImage(background);

        EpisodePicker.Visibility = item.Videos.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        EpisodePicker.ItemsSource = item.Videos;
        if (item.Videos.Count > 0 && EpisodePicker.SelectedIndex < 0) EpisodePicker.SelectedIndex = 0;
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
            StreamStatusText.Text = "Add a provider from Add-ons, or sign in to sync your Stremio providers.";
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
        StreamStatusText.Text = $"Checking {addons.Count} provider(s) for playable streams…";
        StreamList.ItemsSource = null;
        var videoId = (EpisodePicker.SelectedItem as VideoItem)?.Id ?? _item.Id;

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
                StreamStatusText.Text = $"{ordered.Count} playable stream(s) found. Checking {requests.Count} provider(s)…";
            }

            var playable = OrderAndDeduplicateStreams(streams);
            StreamList.ItemsSource = playable;
            _streamsLoaded = true;
            StreamStatusText.Text = playable.Count == 0
                ? "No playable streams found. Try another episode or check your providers in Add-ons."
                : $"{playable.Count} playable stream(s) ready.";
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
        stream.Resolution.Kind == StreamResolutionKind.NativeDirect ||
        stream.Resolution.Kind == StreamResolutionKind.RequiresStreamingService &&
        PrototypeSettings.GetStreamingServiceUrl() is not null;

    private static List<StreamItem> OrderAndDeduplicateStreams(IEnumerable<StreamItem> streams) => streams
        .GroupBy(StreamIdentity, StringComparer.OrdinalIgnoreCase)
        .Select(group => group.First())
        .OrderByDescending(stream => stream.Resolution.Kind == StreamResolutionKind.NativeDirect)
        .ThenBy(stream => stream.Name ?? stream.Title ?? stream.Provider)
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

    private async void OpenStream(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not StreamItem stream || _item is null) return;
        if (stream.Resolution.Kind == StreamResolutionKind.NativeDirect && stream.Resolution.PlaybackUri is not null)
        {
            Frame.Navigate(typeof(PlayerPage), new PlaybackRequest(stream.Resolution.PlaybackUri, _item.Name, stream.Provider));
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
                return;
            }

            BusyIndicator.IsActive = true;
            StreamStatusText.Text = "Preparing stream through Stremio Service…";
            try
            {
                _request?.Cancel();
                _request = new CancellationTokenSource();
                var playbackUri = await _streamingServiceClient.ResolveTorrentAsync(serviceUrl, stream, _request.Token);
                Frame.Navigate(typeof(PlayerPage), new PlaybackRequest(playbackUri, _item.Name, stream.Provider + " via Stremio Service"));
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                BusyIndicator.IsActive = false;
                StreamStatusText.Text = "Stream preparation failed.";
                DiagnosticsService.Current.Error("streaming-service", exception.Message);
                await new ContentDialog
                {
                    Title = "Could not prepare stream",
                    Content = "Check that your playback server is running. Reconnect from Server under Developer / Advanced, then try again or choose another stream.",
                    CloseButtonText = "Choose another stream"
                }.ShowAsync();
            }
            return;
        }

        await new ContentDialog
        {
            Title = "Stream is not native-direct",
            Content = stream.Resolution.Label + ". This prototype deliberately does not process torrents, archives, external pages, or proxy-header streams on the console.",
            CloseButtonText = "Choose another stream"
        }.ShowAsync();
    }
}

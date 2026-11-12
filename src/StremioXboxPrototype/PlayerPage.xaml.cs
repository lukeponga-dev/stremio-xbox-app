using System.Diagnostics;
using System.Net.Http;
using StremioXboxPrototype.Models;
using StremioXboxPrototype.Services;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using Windows.UI.Xaml.Media.Imaging;

namespace StremioXboxPrototype;

public sealed partial class PlayerPage : Page
{
    private static readonly HttpClient MediaProbe = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly Stopwatch _openTimer = new();
    private PlaybackRequest? _request;
    private CancellationTokenSource? _opening;
    private DispatcherTimer? _overlayTimer;
    private bool _active;
    private bool _mediaReady;
    private bool _completed;
    private readonly DispatcherTimer _progressTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    private readonly DispatcherTimer _loadingTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private DateTimeOffset _loadingStartedAt;
    private string _loadingStage = "Opening media";

    public PlayerPage()
    {
        InitializeComponent();
        _progressTimer.Tick += (_, _) => SaveProgress();
        _loadingTimer.Tick += (_, _) => UpdateLoadingStatus();
    }

    private void ViewportSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var layout = TvLayout.GetViewportLayout(e.NewSize.Width, e.NewSize.Height);
        var insets = new Thickness(layout.HorizontalInset, layout.VerticalInset,
            layout.HorizontalInset, layout.VerticalInset);
        ControllerHintSurface.Margin = insets;
        StatusOverlay.Margin = new Thickness(insets.Left, insets.Top + 64, insets.Right, insets.Bottom);
        StatusOverlay.MaxWidth = Math.Max(1, Math.Min(760, e.NewSize.Width - layout.HorizontalInset * 2));
        StatusOverlay.MaxHeight = Math.Max(1, e.NewSize.Height * 0.6);
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _request = e.Parameter as PlaybackRequest;
        var pending = e.Parameter as StreamPlaybackRequest;
        if (_request is null && pending is null) return;
        _active = true;
        _mediaReady = _completed = false;
        var opening = new CancellationTokenSource();
        _opening = opening;

        TitleText.Text = pending?.Title ?? _request!.Title;
        LoadingTitle.Text = TitleText.Text;
        var artwork = pending?.Item?.Background ?? _request?.Item?.Background;
        LoadingBackdrop.Source = Uri.TryCreate(artwork, UriKind.Absolute, out var artworkUri)
            ? new BitmapImage(artworkUri) : null;
        LoadingArtwork.Visibility = Visibility.Visible;
        ArtworkSpinner.IsActive = true;
        StatusOverlay.Visibility = Visibility.Visible;
        LoadingIndicator.Visibility = Visibility.Visible;
        SetLoadingStage(pending is not null
            ? $"Connecting to {pending.Stream.Provider}… Preparing your stream"
            : $"Loading · {_request!.Source}");
        Player.MediaPlayer.MediaOpened += MediaOpened;
        Player.MediaPlayer.MediaFailed += MediaFailed;
        Player.MediaPlayer.MediaEnded += MediaEnded;
        Player.MediaPlayer.PlaybackSession.BufferingStarted += BufferingStarted;
        Player.MediaPlayer.PlaybackSession.BufferingEnded += BufferingEnded;
        Player.MediaPlayer.PlaybackSession.PlaybackStateChanged += PlaybackStateChanged;
        Player.MediaPlayer.IsMuted = false;
        Player.MediaPlayer.Volume = 1;
        _openTimer.Restart();
        Player.Focus(FocusState.Programmatic);
        try
        {
            // Yield to the UI before preparing the source so the player can appear immediately.
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, () => { });
            opening.Token.ThrowIfCancellationRequested();
            if (pending is not null)
            {
                SetLoadingStage($"Preparing torrent through {pending.Stream.Provider}");
                var uri = await new StremioStreamingServiceClient().ResolveTorrentAsync(
                    pending.ServiceUri, pending.Stream, opening.Token);
                opening.Token.ThrowIfCancellationRequested();
                _request = new PlaybackRequest(uri, pending.Title, pending.Stream.Provider, pending.Item, pending.VideoId);
            }
            SetLoadingStage($"Opening video · {_request!.Source}");
            using (var probeTimeout = CancellationTokenSource.CreateLinkedTokenSource(opening.Token))
            {
                probeTimeout.CancelAfter(TimeSpan.FromSeconds(45));
                using var probeRequest = new HttpRequestMessage(HttpMethod.Get, _request.Uri);
                probeRequest.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);
                using var probeResponse = await MediaProbe.SendAsync(probeRequest,
                    HttpCompletionOption.ResponseHeadersRead, probeTimeout.Token);
                if (!probeResponse.IsSuccessStatusCode)
                    throw new HttpRequestException($"Media server returned HTTP {(int)probeResponse.StatusCode} ({probeResponse.ReasonPhrase})");
                var mediaType = probeResponse.Content.Headers.ContentType?.MediaType;
                if (mediaType is "text/html" or "application/json" or "application/xml")
                    throw new InvalidDataException($"The URL returned {mediaType} instead of playable media");
            }
            opening.Token.ThrowIfCancellationRequested();
            DiagnosticsService.Current.Info("player", $"Open media from {_request.Uri.Host}");
            Player.Source = MediaSource.CreateFromUri(_request.Uri);
        }
        catch (OperationCanceledException) when (opening.IsCancellationRequested) { }
        catch (OperationCanceledException)
        {
            if (_active) ShowFailure("The server did not return media headers within 45 seconds. Try another source or check the streaming server");
        }
        catch (Exception exception)
        {
            if (_active) ShowFailure(exception.Message);
            DiagnosticsService.Current.Error("player", exception.Message);
        }
        finally
        {
            if (ReferenceEquals(_opening, opening)) _opening = null;
            opening.Dispose();
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        // Capture the final position before clearing the media source resets it.
        SaveProgress();
        _progressTimer.Stop();
        _active = false;
        _opening?.Cancel();
        _overlayTimer?.Stop();
        _loadingTimer.Stop();
        Player.MediaPlayer.MediaOpened -= MediaOpened;
        Player.MediaPlayer.MediaFailed -= MediaFailed;
        Player.MediaPlayer.MediaEnded -= MediaEnded;
        Player.MediaPlayer.PlaybackSession.BufferingStarted -= BufferingStarted;
        Player.MediaPlayer.PlaybackSession.BufferingEnded -= BufferingEnded;
        Player.MediaPlayer.PlaybackSession.PlaybackStateChanged -= PlaybackStateChanged;
        Player.MediaPlayer.Pause();
        Player.Source = null;
        base.OnNavigatedFrom(e);
    }

    private async void MediaOpened(MediaPlayer sender, object args)
    {
        var elapsed = _openTimer.ElapsedMilliseconds;
        DiagnosticsService.Current.Info("player", $"Media opened in {elapsed} ms");
        try
        {
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                if (!_active) return;
                _mediaReady = true;
                LoadingArtwork.Visibility = Visibility.Collapsed;
                ArtworkSpinner.IsActive = false;
                _loadingTimer.Stop();
                // Resume only after the new source exposes its seek range. Match
                // the episode as well as the title, and reject out-of-range offsets.
                if (_request?.Item is MetaItem item && sender.PlaybackSession.CanSeek)
                {
                    var progress = PrototypeSettings.GetWatchProgress(item.Type, item.Id, _request.VideoId ?? item.Id);
                    if (progress is not null && progress.PositionSeconds < sender.PlaybackSession.NaturalDuration.TotalSeconds)
                        sender.PlaybackSession.Position = TimeSpan.FromSeconds(progress.PositionSeconds);
                }
                _progressTimer.Start();
                LoadingIndicator.Visibility = Visibility.Collapsed;
                Player.MediaPlayer.IsMuted = false;
                Player.MediaPlayer.Volume = 1;
                Player.MediaPlayer.Play();
                StatusText.Text = $"Playing · opened in {elapsed} ms · {_request?.Source}";
                HideOverlaySoon();
            });
        }
        catch (OperationCanceledException) { }
    }

    private async void MediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        var detail = string.IsNullOrWhiteSpace(args.ErrorMessage)
            ? args.Error.ToString()
            : $"{args.Error} ({args.ErrorMessage})";
        var source = _request?.Uri.AbsoluteUri ?? "unknown source";
        DiagnosticsService.Current.Error("player", $"Media failed: {detail}; source={source}");
        try
        {
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                if (_active) ShowFailure(detail);
            });
        }
        catch (OperationCanceledException) { }
    }

    private void ShowFailure(string message)
    {
        // Callers marshal failures to the UI thread before touching XAML timers.
        _loadingTimer.Stop();
        _progressTimer.Stop();
        _overlayTimer?.Stop();
        _active = false;
        _mediaReady = false;
        ArtworkSpinner.IsActive = false;
        LoadingIndicator.Visibility = Visibility.Collapsed;
        StatusOverlay.Visibility = Visibility.Visible;
        StatusText.Text = $"Could not play this stream: {message}. Press B to choose another source.";
    }

    private async void MediaEnded(MediaPlayer sender, object args)
    {
        DiagnosticsService.Current.Info("player", "Playback ended");
        try
        {
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                if (!_active) return;
                _completed = true;
                SaveProgress();
                _progressTimer.Stop();
                LoadingIndicator.Visibility = Visibility.Collapsed;
                ShowPlayerMessage("Playback complete. Press A to replay or B to choose another source.", hideAutomatically: false);
            });
        }
        catch (OperationCanceledException) { }
    }

    private void SaveProgress()
    {
        // Manual playback-lab URLs have no catalog item and stay out of history.
        // Failed opens must not overwrite a previously saved resume position.
        if (!_mediaReady || _request?.Item is not MetaItem item) return;
        var session = Player.MediaPlayer.PlaybackSession;
        PrototypeSettings.SaveWatchProgress(item, _request.VideoId ?? item.Id,
            session.Position.TotalSeconds, session.NaturalDuration.TotalSeconds, _completed);
    }

    private async void BufferingStarted(MediaPlaybackSession sender, object args)
    {
        DiagnosticsService.Current.Info("player", "Buffering started");
        await RunPlaybackUiAsync(() =>
        {
        if (!_active) return;
        LoadingIndicator.Visibility = Visibility.Visible;
        ShowPlayerMessage("Buffering…", hideAutomatically: false);
        });
    }

    private async void BufferingEnded(MediaPlaybackSession sender, object args)
    {
        DiagnosticsService.Current.Info("player", "Buffering ended");
        await RunPlaybackUiAsync(() =>
        {
        if (!_active || _completed) return;
        LoadingIndicator.Visibility = Visibility.Collapsed;
        StatusText.Text = "Playing";
        HideOverlaySoon();
        });
    }

    private async void PlaybackStateChanged(MediaPlaybackSession sender, object args)
    {
        await RunPlaybackUiAsync(() =>
        {
        if (!_active || _completed) return;
        if (sender.PlaybackState == MediaPlaybackState.Playing)
            StatusText.Text = "Playing";
        else if (sender.PlaybackState == MediaPlaybackState.Paused)
            ShowPlayerMessage("Paused", hideAutomatically: false);
        });
    }

    private async Task RunPlaybackUiAsync(Action update)
    {
        try
        {
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                if (_active) update();
            });
        }
        catch (OperationCanceledException) { }
    }

    private void BackToSources(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack) Frame.GoBack();
    }

    private void PlayerPageKeyDown(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.GamepadB || e.Key == VirtualKey.Escape)
        {
            if (Frame.CanGoBack) Frame.GoBack();
            e.Handled = true;
            return;
        }

        if (e.Key == VirtualKey.GamepadA)
        {
            TogglePlayback();
            e.Handled = true;
        }
        else if (e.Key is VirtualKey.GamepadLeftShoulder or VirtualKey.GamepadDPadLeft)
        {
            SeekBy(TimeSpan.FromSeconds(-10));
            e.Handled = true;
        }
        else if (e.Key is VirtualKey.GamepadRightShoulder or VirtualKey.GamepadDPadRight)
        {
            SeekBy(TimeSpan.FromSeconds(10));
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.GamepadX)
        {
            Player.MediaPlayer.IsMuted = !Player.MediaPlayer.IsMuted;
            ControllerHintText.Text = Player.MediaPlayer.IsMuted
                ? "A  Play/Pause     LB/RB  Seek 10s     B  Back     X  Unmute"
                : "A  Play/Pause     LB/RB  Seek 10s     B  Back     X  Mute";
            ShowPlayerMessage(Player.MediaPlayer.IsMuted ? "Audio muted" : "Audio on", hideAutomatically: false);
            DiagnosticsService.Current.Info("player", Player.MediaPlayer.IsMuted ? "Audio muted" : "Audio unmuted");
            e.Handled = true;
        }
    }

    private void TogglePlayback()
    {
        if (!_mediaReady) return;
        var session = Player.MediaPlayer.PlaybackSession;
        if (_completed)
        {
            if (session.CanSeek) session.Position = TimeSpan.Zero;
            _completed = false;
            _progressTimer.Start();
            Player.MediaPlayer.Play();
            ShowPlayerMessage("Replaying", hideAutomatically: true);
            return;
        }

        if (session.PlaybackState == MediaPlaybackState.Playing)
        {
            Player.MediaPlayer.Pause();
            ShowPlayerMessage("Paused", hideAutomatically: false);
        }
        else
        {
            Player.MediaPlayer.Play();
            ShowPlayerMessage("Playing", hideAutomatically: true);
        }
    }

    private void SeekBy(TimeSpan offset)
    {
        var session = Player.MediaPlayer.PlaybackSession;
        if (!_mediaReady || !session.CanSeek) return;
        var target = session.Position + offset;
        if (target < TimeSpan.Zero) target = TimeSpan.Zero;
        if (session.NaturalDuration > TimeSpan.Zero && target > session.NaturalDuration)
            target = session.NaturalDuration;
        session.Position = target;
        ShowPlayerMessage($"{(offset < TimeSpan.Zero ? "Rewound" : "Skipped forward")} 10 seconds", hideAutomatically: true);
        DiagnosticsService.Current.Info("player", $"Seeked to {target.TotalSeconds:0} seconds");
    }

    private void ShowPlayerMessage(string message, bool hideAutomatically)
    {
        StatusOverlay.Visibility = Visibility.Visible;
        StatusText.Text = message;
        if (hideAutomatically) HideOverlaySoon();
        else _overlayTimer?.Stop();
    }

    private void HideOverlaySoon()
    {
        _overlayTimer?.Stop();
        _overlayTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _overlayTimer.Tick += (_, _) =>
        {
            StatusOverlay.Visibility = Visibility.Collapsed;
            _overlayTimer.Stop();
        };
        _overlayTimer.Start();
    }

    private void SetLoadingStage(string stage)
    {
        _loadingStage = stage;
        _loadingStartedAt = DateTimeOffset.UtcNow;
        StatusText.Text = stage + " · 0s";
        _loadingTimer.Start();
    }

    private void UpdateLoadingStatus()
    {
        if (!_active || _mediaReady) return;
        var seconds = Math.Max(0, (int)(DateTimeOffset.UtcNow - _loadingStartedAt).TotalSeconds);
        StatusText.Text = $"{_loadingStage} · {seconds}s";
    }
}

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

/// <summary>Owns media preparation, native playback, controller overlays, and resume progress.</summary>
/// <remarks>All asynchronous preparation is tied to the current navigation so a stale request cannot replace a newer source.</remarks>
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
    private readonly DispatcherTimer _progressTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _updatingProgress;
    private bool _adjustingSeek;
    private MediaPlaybackItem? _playbackItem;
    private int _selectedSubtitleTrack = -1;
    private readonly DispatcherTimer _loadingTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private DateTimeOffset _loadingStartedAt;
    private string _loadingStage = "Opening media";

    public PlayerPage()
    {
        InitializeComponent();
        _progressTimer.Tick += (_, _) =>
        {
            SaveProgress();
            UpdatePlaybackProgress();
        };
        _loadingTimer.Tick += (_, _) => UpdateLoadingStatus();
    }

    private void ViewportSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var layout = TvLayout.GetViewportLayout(e.NewSize.Width, e.NewSize.Height);
        var insets = new Thickness(layout.HorizontalInset, layout.VerticalInset,
            layout.HorizontalInset, layout.VerticalInset);
        ControllerHintSurface.Margin = insets;
        ControllerHintSurface.Padding = new Thickness(e.NewSize.Width < 760 ? 10 : 14, 8, e.NewSize.Width < 760 ? 10 : 14, 8);
        ControllerHintText.FontSize = e.NewSize.Width < 760 ? 12 : e.NewSize.Width < 1200 ? 14 : 16;
        BackSourcesButton.Margin = new Thickness(layout.HorizontalInset, layout.VerticalInset, 0, 0);
        BackSourcesButton.FontSize = e.NewSize.Width < 760 ? 14 : 17;
        LoadingTitle.FontSize = Math.Clamp(e.NewSize.Width * 0.055, 25, 48);
        StatusOverlay.Margin = new Thickness(insets.Left, insets.Top + (e.NewSize.Height < 600 ? 48 : 64), insets.Right, insets.Bottom);
        StatusOverlay.Padding = new Thickness(e.NewSize.Width < 760 ? 14 : 24);
        StatusOverlay.MaxWidth = Math.Max(1, Math.Min(760, e.NewSize.Width - layout.HorizontalInset * 2));
        StatusOverlay.MaxHeight = Math.Max(1, e.NewSize.Height * 0.62);
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
            var mediaSource = MediaSource.CreateFromUri(_request.Uri);
            _playbackItem = new MediaPlaybackItem(mediaSource);
            _playbackItem.AudioTracksChanged += AudioTracksChanged;
            _playbackItem.TimedMetadataTracksChanged += TimedMetadataTracksChanged;
            Player.Source = _playbackItem;
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
        if (_playbackItem is not null)
        {
            _playbackItem.AudioTracksChanged -= AudioTracksChanged;
            _playbackItem.TimedMetadataTracksChanged -= TimedMetadataTracksChanged;
            _playbackItem = null;
        }
        PlaybackOptionsPanel.Visibility = Visibility.Collapsed;
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
                var duration = sender.PlaybackSession.NaturalDuration;
                PlaybackProgressSlider.Maximum = Math.Max(1, duration.TotalSeconds);
                PlaybackProgressSlider.IsEnabled = sender.PlaybackSession.CanSeek && duration > TimeSpan.Zero;
                DurationTimeText.Text = FormatPlaybackTime(duration);
                UpdatePlaybackProgress();
                UpdatePlayPauseButton();
                UpdateMuteButton();
                BuildPlaybackOptionButtons();
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
        UpdatePlayPauseButton();
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

    private void PlayPauseClicked(object sender, RoutedEventArgs e) => TogglePlayback();

    private void RewindClicked(object sender, RoutedEventArgs e) => SeekBy(TimeSpan.FromSeconds(-10));

    private void ForwardClicked(object sender, RoutedEventArgs e) => SeekBy(TimeSpan.FromSeconds(10));

    private void MuteClicked(object sender, RoutedEventArgs e)
    {
        Player.MediaPlayer.IsMuted = !Player.MediaPlayer.IsMuted;
        UpdateMuteButton();
        ShowPlayerMessage(Player.MediaPlayer.IsMuted ? "Audio muted" : "Audio on", hideAutomatically: true);
    }

    private void PlaybackProgressGotFocus(object sender, RoutedEventArgs e) => _adjustingSeek = true;

    private void PlaybackProgressLostFocus(object sender, RoutedEventArgs e) => _adjustingSeek = false;

    private void PlaybackProgressValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_updatingProgress || !_adjustingSeek || !_mediaReady || !Player.MediaPlayer.PlaybackSession.CanSeek)
            return;

        var target = TimeSpan.FromSeconds(Math.Max(0, e.NewValue));
        Player.MediaPlayer.PlaybackSession.Position = target;
        CurrentTimeText.Text = FormatPlaybackTime(target);
        ShowPlaybackChrome(hideAutomatically: true);
    }

    private void UpdatePlaybackProgress()
    {
        if (!_mediaReady) return;
        var session = Player.MediaPlayer.PlaybackSession;
        var position = session.Position;
        var duration = session.NaturalDuration;
        _updatingProgress = true;
        try
        {
            if (!_adjustingSeek)
                PlaybackProgressSlider.Value = Math.Clamp(position.TotalSeconds, PlaybackProgressSlider.Minimum, PlaybackProgressSlider.Maximum);
            CurrentTimeText.Text = FormatPlaybackTime(position);
            DurationTimeText.Text = FormatPlaybackTime(duration);
        }
        finally
        {
            _updatingProgress = false;
        }
    }

    private static string FormatPlaybackTime(TimeSpan time)
    {
        if (time < TimeSpan.Zero || double.IsNaN(time.TotalSeconds) || double.IsInfinity(time.TotalSeconds))
            return "0:00";
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{(int)time.TotalMinutes}:{time.Seconds:00}";
    }

    private void UpdatePlayPauseButton()
    {
        if (!_mediaReady) return;
        var isPlaying = Player.MediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;
        PlayPauseButton.Content = isPlaying ? "Pause" : "Play";
        Windows.UI.Xaml.Automation.AutomationProperties.SetName(PlayPauseButton, isPlaying ? "Pause playback" : "Resume playback");
    }

    private void UpdateMuteButton()
    {
        MuteButton.Content = Player.MediaPlayer.IsMuted ? "Unmute" : "Mute";
        Windows.UI.Xaml.Automation.AutomationProperties.SetName(MuteButton, Player.MediaPlayer.IsMuted ? "Unmute audio" : "Mute audio");
    }

    private void AudioTracksChanged(MediaPlaybackItem sender, Windows.Foundation.Collections.IVectorChangedEventArgs args)
    {
        _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, BuildPlaybackOptionButtons);
    }

    private void TimedMetadataTracksChanged(MediaPlaybackItem sender, Windows.Foundation.Collections.IVectorChangedEventArgs args)
    {
        _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, BuildPlaybackOptionButtons);
    }

    private void BuildPlaybackOptionButtons()
    {
        if (_playbackItem is null) return;

        AudioTrackOptions.Children.Clear();
        if (_playbackItem.AudioTracks.Count == 0)
        {
            AudioTrackOptions.Children.Add(CreateOptionLabel("No alternate audio tracks reported by this stream"));
        }
        else
        {
            for (var index = 0; index < _playbackItem.AudioTracks.Count; index++)
            {
                var trackIndex = index;
                var track = _playbackItem.AudioTracks[index];
                var label = string.IsNullOrWhiteSpace(track.Label) ? $"Audio {index + 1}" : track.Label;
                var button = CreateOptionButton(label, _playbackItem.AudioTracks.SelectedIndex == index);
                button.Click += (_, _) =>
                {
                    if (_playbackItem is null) return;
                    _playbackItem.AudioTracks.SelectedIndex = trackIndex;
                    BuildPlaybackOptionButtons();
                };
                AudioTrackOptions.Children.Add(button);
            }
        }

        SubtitleTrackOptions.Children.Clear();
        var offButton = CreateOptionButton("Off", _selectedSubtitleTrack < 0);
        offButton.Click += (_, _) =>
        {
            if (_playbackItem is null) return;
            for (uint index = 0; index < _playbackItem.TimedMetadataTracks.Count; index++)
                _playbackItem.TimedMetadataTracks.SetPresentationMode(index, TimedMetadataTrackPresentationMode.Disabled);
            _selectedSubtitleTrack = -1;
            BuildPlaybackOptionButtons();
        };
        SubtitleTrackOptions.Children.Add(offButton);

        var subtitleCount = _playbackItem.TimedMetadataTracks.Count;
        if (subtitleCount == 0)
        {
            SubtitleTrackOptions.Children.Add(CreateOptionLabel("No subtitle tracks reported by this stream"));
        }
        else
        {
            for (var index = 0; index < subtitleCount; index++)
            {
                var trackIndex = index;
                var track = _playbackItem.TimedMetadataTracks[index];
                var label = string.IsNullOrWhiteSpace(track.Label) ? $"Subtitle {index + 1}" : track.Label;
                var button = CreateOptionButton(label, _selectedSubtitleTrack == index);
                button.Click += (_, _) =>
                {
                    if (_playbackItem is null) return;
                    for (uint trackNumber = 0; trackNumber < _playbackItem.TimedMetadataTracks.Count; trackNumber++)
                        _playbackItem.TimedMetadataTracks.SetPresentationMode(trackNumber,
                            trackNumber == trackIndex ? TimedMetadataTrackPresentationMode.PlatformPresented
                                : TimedMetadataTrackPresentationMode.Disabled);
                    _selectedSubtitleTrack = trackIndex;
                    BuildPlaybackOptionButtons();
                };
                SubtitleTrackOptions.Children.Add(button);
            }
        }

        PlaybackSpeedOptions.Children.Clear();
        var currentRate = Player.MediaPlayer.PlaybackSession.PlaybackRate;
        foreach (var speed in new[] { 0.5, 0.75, 1.0, 1.25, 1.5, 2.0 })
        {
            var rate = speed;
            var button = CreateOptionButton($"{speed:0.##}x", Math.Abs(currentRate - speed) < 0.01);
            button.MinWidth = 62;
            button.Click += (_, _) =>
            {
                try
                {
                    Player.MediaPlayer.PlaybackSession.PlaybackRate = rate;
                    BuildPlaybackOptionButtons();
                    StatusText.Text = $"Playback speed · {rate:0.##}x";
                }
                catch (Exception exception)
                {
                    DiagnosticsService.Current.Info("player", $"Playback rate {rate:0.##}x is not supported: {exception.Message}");
                    StatusText.Text = $"This stream does not support {rate:0.##}x playback";
                }
            };
            PlaybackSpeedOptions.Children.Add(button);
        }
    }

    private static Button CreateOptionButton(string label, bool selected)
    {
        var button = new Button
        {
            Content = selected ? $"✓  {label}" : label,
            HorizontalAlignment = Windows.UI.Xaml.HorizontalAlignment.Stretch,
            MinHeight = 52,
            FontSize = 17,
            FontWeight = selected ? Windows.UI.Text.FontWeights.Bold : Windows.UI.Text.FontWeights.SemiBold,
            Background = selected ? new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 175, 255))
                : new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 35, 47, 64)),
            Foreground = selected ? new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.Black)
                : new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.White)
        };
        Windows.UI.Xaml.Automation.AutomationProperties.SetName(button, label);
        return button;
    }

    private static TextBlock CreateOptionLabel(string text) => new()
    {
        Text = text,
        FontSize = 15,
        TextWrapping = Windows.UI.Xaml.TextWrapping.Wrap,
        Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 185, 196, 210)),
        Margin = new Thickness(4, 6, 4, 6)
    };

    private void TogglePlaybackOptions()
    {
        if (!_mediaReady) return;
        var show = PlaybackOptionsPanel.Visibility != Visibility.Visible;
        PlaybackOptionsPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show)
        {
            _overlayTimer?.Stop();
            BuildPlaybackOptionButtons();
            var firstOption = AudioTrackOptions.Children.Count > 0
                ? AudioTrackOptions.Children[0] as Control
                : null;
            firstOption ??= SubtitleTrackOptions.Children.Count > 0
                ? SubtitleTrackOptions.Children[0] as Control
                : null;
            firstOption?.Focus(FocusState.Programmatic);
        }
        else
        {
            ShowPlaybackChrome(hideAutomatically: true);
        }
    }

    private void BackToSources(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack) Frame.GoBack();
    }

    private void PlayerPageKeyDown(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        // Leave standard transport input (A, D-pad, shoulders and triggers) to
        // MediaPlayerElement's built-in Xbox transport controls.
        if (e.Key == VirtualKey.GamepadB || e.Key == VirtualKey.Escape)
        {
            if (PlaybackOptionsPanel.Visibility == Visibility.Visible)
            {
                PlaybackOptionsPanel.Visibility = Visibility.Collapsed;
                ShowPlaybackChrome(hideAutomatically: true);
            }
            else if (Frame.CanGoBack)
            {
                Frame.GoBack();
            }
            e.Handled = true;
            return;
        }

        if (e.Key == VirtualKey.GamepadY)
        {
            TogglePlaybackOptions();
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
            UpdatePlayPauseButton();
            ShowPlayerMessage("Replaying", hideAutomatically: true);
            return;
        }

        if (session.PlaybackState == MediaPlaybackState.Playing)
        {
            Player.MediaPlayer.Pause();
            UpdatePlayPauseButton();
            ShowPlayerMessage("Paused", hideAutomatically: false);
        }
        else
        {
            Player.MediaPlayer.Play();
            UpdatePlayPauseButton();
            ShowPlayerMessage("Playing", hideAutomatically: true);
        }
    }

    private void AdjustVolume(double delta)
    {
        var player = Player.MediaPlayer;
        player.IsMuted = false;
        player.Volume = Math.Clamp(player.Volume + delta, 0, 1);
        UpdateMuteButton();
        var percent = (int)Math.Round(player.Volume * 100);
        ShowPlayerMessage($"Volume · {percent}%", hideAutomatically: true);
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
        UpdatePlaybackProgress();
        ShowPlayerMessage($"{(offset < TimeSpan.Zero ? "Rewound" : "Skipped forward")} {Math.Abs(offset.TotalSeconds):0} seconds", hideAutomatically: true);
        DiagnosticsService.Current.Info("player", $"Seeked to {target.TotalSeconds:0} seconds");
    }

    private void ShowPlaybackChrome(bool hideAutomatically)
    {
        StatusOverlay.Visibility = Visibility.Visible;
        BackSourcesButton.Visibility = Visibility.Visible;
        PlaybackControls.Visibility = Visibility.Collapsed;
        ControllerHintSurface.Visibility = Visibility.Collapsed;
        if (hideAutomatically) HideOverlaySoon();
        else _overlayTimer?.Stop();
    }

    private void ShowPlayerMessage(string message, bool hideAutomatically)
    {
        StatusOverlay.Visibility = Visibility.Visible;
        BackSourcesButton.Visibility = Visibility.Visible;
        PlaybackControls.Visibility = Visibility.Collapsed;
        ControllerHintSurface.Visibility = Visibility.Collapsed;
        StatusText.Text = message;
        if (hideAutomatically) HideOverlaySoon();
        else _overlayTimer?.Stop();
    }

    private bool IsPlaybackControlFocused()
    {
        // Only the optional track/speed panel needs to keep the informational
        // overlay alive. Native transport controls manage their own visibility.
        return PlaybackOptionsPanel.Visibility == Visibility.Visible;
    }

    private void HideOverlaySoon()
    {
        _overlayTimer?.Stop();
        _overlayTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _overlayTimer.Tick += (_, _) =>
        {
            if (IsPlaybackControlFocused())
            {
                _overlayTimer.Stop();
                return;
            }

            StatusOverlay.Visibility = Visibility.Collapsed;
            BackSourcesButton.Visibility = Visibility.Collapsed;
            PlaybackControls.Visibility = Visibility.Collapsed;
            ControllerHintSurface.Visibility = Visibility.Collapsed;
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

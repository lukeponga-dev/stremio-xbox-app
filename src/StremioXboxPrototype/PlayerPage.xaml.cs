using System.Diagnostics;
using StremioXboxPrototype.Models;
using StremioXboxPrototype.Services;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace StremioXboxPrototype;

public sealed partial class PlayerPage : Page
{
    private readonly Stopwatch _openTimer = new();
    private PlaybackRequest? _request;
    private CancellationTokenSource? _opening;
    private DispatcherTimer? _overlayTimer;
    private bool _active;

    public PlayerPage() => InitializeComponent();

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _request = e.Parameter as PlaybackRequest;
        var pending = e.Parameter as StreamPlaybackRequest;
        if (_request is null && pending is null) return;
        _active = true;
        var opening = new CancellationTokenSource();
        _opening = opening;

        TitleText.Text = pending?.Title ?? _request!.Title;
        StatusOverlay.Visibility = Visibility.Visible;
        LoadingIndicator.Visibility = Visibility.Visible;
        StatusText.Text = pending is not null
            ? $"Connecting to {pending.Stream.Provider}… Preparing your stream."
            : $"Loading · {_request!.Source}";
        Player.MediaPlayer.MediaOpened += MediaOpened;
        Player.MediaPlayer.MediaFailed += MediaFailed;
        Player.MediaPlayer.MediaEnded += MediaEnded;
        Player.MediaPlayer.PlaybackSession.BufferingStarted += BufferingStarted;
        Player.MediaPlayer.PlaybackSession.BufferingEnded += BufferingEnded;
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
                var uri = await new StremioStreamingServiceClient().ResolveTorrentAsync(
                    pending.ServiceUri, pending.Stream, opening.Token);
                opening.Token.ThrowIfCancellationRequested();
                _request = new PlaybackRequest(uri, pending.Title, pending.Stream.Provider);
            }
            StatusText.Text = $"Loading video · {_request!.Source}";
            DiagnosticsService.Current.Info("player", $"Open {_request.Uri}");
            Player.Source = MediaSource.CreateFromUri(_request.Uri);
        }
        catch (OperationCanceledException) when (opening.IsCancellationRequested) { }
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
        _active = false;
        _opening?.Cancel();
        _overlayTimer?.Stop();
        Player.MediaPlayer.MediaOpened -= MediaOpened;
        Player.MediaPlayer.MediaFailed -= MediaFailed;
        Player.MediaPlayer.MediaEnded -= MediaEnded;
        Player.MediaPlayer.PlaybackSession.BufferingStarted -= BufferingStarted;
        Player.MediaPlayer.PlaybackSession.BufferingEnded -= BufferingEnded;
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
                LoadingIndicator.Visibility = Visibility.Collapsed;
                Player.MediaPlayer.IsMuted = false;
                Player.MediaPlayer.Volume = 1;
                Player.MediaPlayer.Play();
                StatusText.Text = $"Playing · opened in {elapsed} ms · {_request?.Source}";
                _overlayTimer?.Stop();
                _overlayTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
                _overlayTimer.Tick += (_, _) =>
                {
                    StatusOverlay.Visibility = Visibility.Collapsed;
                    _overlayTimer.Stop();
                };
                _overlayTimer.Start();
            });
        }
        catch (OperationCanceledException) { }
    }

    private async void MediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        DiagnosticsService.Current.Error("player", $"{args.Error}: {args.ErrorMessage}");
        try
        {
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                if (_active) ShowFailure(args.ErrorMessage);
            });
        }
        catch (OperationCanceledException) { }
    }

    private void ShowFailure(string message)
    {
        _overlayTimer?.Stop();
        LoadingIndicator.Visibility = Visibility.Collapsed;
        StatusOverlay.Visibility = Visibility.Visible;
        StatusText.Text = $"Could not play this stream: {message}. Press B to choose another source.";
    }

    private void MediaEnded(MediaPlayer sender, object args) => DiagnosticsService.Current.Info("player", "Playback ended");
    private void BufferingStarted(MediaPlaybackSession sender, object args) => DiagnosticsService.Current.Info("player", "Buffering started");
    private void BufferingEnded(MediaPlaybackSession sender, object args) => DiagnosticsService.Current.Info("player", "Buffering ended");

    private void PlayerPageKeyDown(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.GamepadB || e.Key == VirtualKey.Escape)
        {
            if (Frame.CanGoBack) Frame.GoBack();
            e.Handled = true;
            return;
        }

        if (e.Key != VirtualKey.GamepadX) return;
        Player.MediaPlayer.IsMuted = !Player.MediaPlayer.IsMuted;
        ControllerHintText.Text = Player.MediaPlayer.IsMuted ? "B  Back     X  Unmute" : "B  Back     X  Mute";
        StatusOverlay.Visibility = Visibility.Visible;
        StatusText.Text = Player.MediaPlayer.IsMuted ? "Audio muted" : "Audio on";
        DiagnosticsService.Current.Info("player", Player.MediaPlayer.IsMuted ? "Audio muted" : "Audio unmuted");
        e.Handled = true;
    }
}

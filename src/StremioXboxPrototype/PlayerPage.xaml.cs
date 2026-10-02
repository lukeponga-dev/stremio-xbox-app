using System.Diagnostics;
using StremioXboxPrototype.Models;
using StremioXboxPrototype.Services;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace StremioXboxPrototype;

public sealed partial class PlayerPage : Page
{
    private readonly Stopwatch _openTimer = new();
    private PlaybackRequest? _request;

    public PlayerPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _request = e.Parameter as PlaybackRequest;
        if (_request is null) return;

        TitleText.Text = _request.Title;
        StatusText.Text = $"Opening {_request.Uri.Host} · {_request.Source}";
        Player.MediaPlayer.MediaOpened += MediaOpened;
        Player.MediaPlayer.MediaFailed += MediaFailed;
        Player.MediaPlayer.MediaEnded += MediaEnded;
        Player.MediaPlayer.PlaybackSession.BufferingStarted += BufferingStarted;
        Player.MediaPlayer.PlaybackSession.BufferingEnded += BufferingEnded;
        Player.MediaPlayer.IsMuted = false;
        Player.MediaPlayer.Volume = 1;
        _openTimer.Start();
        DiagnosticsService.Current.Info("player", $"Open {_request.Uri}");
        Player.Source = MediaSource.CreateFromUri(_request.Uri);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
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
                Player.MediaPlayer.IsMuted = false;
                Player.MediaPlayer.Volume = 1;
                Player.MediaPlayer.Play();
                StatusText.Text = $"Playing · opened in {elapsed} ms · {_request?.Source}";
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
                timer.Tick += (_, _) =>
                {
                    StatusOverlay.Visibility = Visibility.Collapsed;
                    timer.Stop();
                };
                timer.Start();
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
                StatusOverlay.Visibility = Visibility.Visible;
                StatusText.Text = $"Playback failed: {args.ErrorMessage}. Press B to return to stream selection.";
            });
        }
        catch (OperationCanceledException) { }
    }

    private void MediaEnded(MediaPlayer sender, object args) => DiagnosticsService.Current.Info("player", "Playback ended");
    private void BufferingStarted(MediaPlaybackSession sender, object args) => DiagnosticsService.Current.Info("player", "Buffering started");
    private void BufferingEnded(MediaPlaybackSession sender, object args) => DiagnosticsService.Current.Info("player", "Buffering ended");
}

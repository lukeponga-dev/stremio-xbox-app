using System;
using System.IO;
using System.Threading.Tasks;
using Windows.ApplicationModel.Activation;
using Windows.Storage;
using Windows.System.Profile;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace StremioXboxPrototype;

sealed partial class App : Application
{
    public App()
    {
        InitializeComponent();

        if (IsXbox)
        {
            try
            {
                ApplicationView.PreferredLaunchWindowingMode = ApplicationViewWindowingMode.FullScreen;
                RequiresPointerMode = ApplicationRequiresPointerMode.WhenRequested;
            }
            catch (Exception ex)
            {
                WriteStartupCrash("App.ctor.XboxSetup", ex);
            }
        }

        Suspending += (_, _) => Services.DiagnosticsService.Current.Info("lifecycle", "Application suspended");
        Resuming += (_, _) => Services.DiagnosticsService.Current.Info("lifecycle", "Application resumed");

        UnhandledException += (_, args) =>
        {
            if (args.Exception is OperationCanceledException)
            {
                args.Handled = true;
                return;
            }

            WriteStartupCrash("App.UnhandledException", args.Exception);
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            WriteStartupCrash("TaskScheduler.UnobservedTaskException", args.Exception);
            args.SetObserved();
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            var frame = Window.Current.Content as Frame;
            if (frame is null)
            {
                frame = new Frame
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    VerticalContentAlignment = VerticalAlignment.Stretch
                };
                frame.NavigationFailed += (_, navArgs) =>
                {
                    WriteStartupCrash($"NavigationFailed:{navArgs.SourcePageType?.Name}", navArgs.Exception);
                };
                Window.Current.Content = frame;
            }

            if (frame.Content is null)
            {
                frame.Navigate(typeof(MainPage));
            }

            Window.Current.Activate();

            if (IsXbox)
            {
                var view = ApplicationView.GetForCurrentView();
                view.SetDesiredBoundsMode(ApplicationViewBoundsMode.UseCoreWindow);
                view.TryEnterFullScreenMode();
            }
        }
        catch (Exception ex)
        {
            WriteStartupCrash("App.OnLaunched", ex);
            throw;
        }
    }

    private static bool IsXbox
    {
        get
        {
            try
            {
                return string.Equals(AnalyticsInfo.VersionInfo.DeviceFamily, "Windows.Xbox", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }

    internal static void WriteStartupCrash(string stage, Exception? ex)
    {
        try
        {
            var path = Path.Combine(ApplicationData.Current.LocalFolder.Path, "startup_crash.log");
            var text = $"[{DateTime.UtcNow:O}] [{stage}]\n{ex}\n====================================\n";
            File.AppendAllText(path, text);
        }
        catch
        {
            // Ignore secondary file I/O failure during crash
        }
    }
}
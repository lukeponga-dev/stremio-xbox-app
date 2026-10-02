using Windows.ApplicationModel.Activation;
using Windows.System.Profile;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace StremioXboxPrototype;

sealed partial class App : Application
{
    public App()
    {
        InitializeComponent();

        // RequiresPointerMode is an Xbox-specific setting. Assigning it while
        // debugging the package on desktop Windows can fail during activation.
        if (AnalyticsInfo.VersionInfo.DeviceFamily == "Windows.Xbox")
        {
            RequiresPointerMode = ApplicationRequiresPointerMode.WhenRequested;
        }

        Suspending += (_, _) => Services.DiagnosticsService.Current.Info("lifecycle", "Application suspended");
        Resuming += (_, _) => Services.DiagnosticsService.Current.Info("lifecycle", "Application resumed");
        UnhandledException += (_, args) =>
        {
            // Network and dispatcher operations are deliberately cancelled during
            // navigation, suspension, and shutdown. They must not terminate UWP.
            if (args.Exception is OperationCanceledException)
            {
                args.Handled = true;
            }
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var frame = Window.Current.Content as Frame;
        if (frame is null)
        {
            frame = new Frame();
            Window.Current.Content = frame;
        }

        if (frame.Content is null)
        {
            frame.Navigate(typeof(MainPage));
        }

        Window.Current.Activate();
    }
}

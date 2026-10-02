using System.Collections.ObjectModel;

namespace StremioXboxPrototype.Services;

public sealed class DiagnosticEntry
{
    public DateTimeOffset Time { get; init; } = DateTimeOffset.Now;
    public string Level { get; init; } = "INFO";
    public string Area { get; init; } = "app";
    public string Message { get; init; } = "";
    public string Display => $"{Time:HH:mm:ss.fff}  {Level,-5}  {Area,-12}  {Message}";
}

public sealed class DiagnosticsService
{
    private const int MaximumEntries = 300;
    public static DiagnosticsService Current { get; } = new();
    public ObservableCollection<DiagnosticEntry> Entries { get; } = new();

    public void Info(string area, string message) => Add("INFO", area, message);
    public void Warn(string area, string message) => Add("WARN", area, message);
    public void Error(string area, string message) => Add("ERROR", area, message);

    private void Add(string level, string area, string message)
    {
        var entry = new DiagnosticEntry { Level = level, Area = area, Message = message };
        _ = AddAsync(entry);
    }

    private async Task AddAsync(DiagnosticEntry entry)
    {
        try
        {
            var dispatcher = Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher;
            await dispatcher.RunAsync(
                Windows.UI.Core.CoreDispatcherPriority.Normal,
                () =>
                {
                    Entries.Insert(0, entry);
                    while (Entries.Count > MaximumEntries)
                    {
                        Entries.RemoveAt(Entries.Count - 1);
                    }
                });
        }
        catch (OperationCanceledException)
        {
            // Normal while the UI dispatcher is suspended or shutting down.
        }
        catch (ObjectDisposedException)
        {
            // Normal after the window has been torn down.
        }
    }
}

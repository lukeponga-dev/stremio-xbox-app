// In-memory adapter for testing production settings logic outside an AppContainer.
namespace Windows.Storage
{
    public enum ApplicationDataCreateDisposition { Always }
    public sealed class ApplicationData
    {
        public static ApplicationData Current { get; } = new();
        public ApplicationDataContainer LocalSettings { get; } = new();
    }
    public sealed class ApplicationDataContainer
    {
        public Dictionary<string, object> Values { get; } = new();
        private readonly Dictionary<string, ApplicationDataContainer> _containers = new();
        public ApplicationDataContainer CreateContainer(string name, ApplicationDataCreateDisposition disposition)
        {
            if (!_containers.TryGetValue(name, out var container))
                _containers[name] = container = new();
            return container;
        }
    }
}
namespace StremioXboxPrototype.Services
{
    public sealed class DiagnosticsService
    {
        public static DiagnosticsService Current { get; } = new();
        public void Warn(string category, string message) => throw new Exception($"{category}: {message}");
    }
}

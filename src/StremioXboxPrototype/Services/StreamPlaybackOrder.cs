using StremioXboxPrototype.Models;

namespace StremioXboxPrototype.Services;

public static class StreamPlaybackOrder
{
    // Availability estimates only: add-on counts can be stale, and a direct
    // URL can still expire or contain a codec the device cannot decode.
    public static IEnumerable<StreamItem> Order(IEnumerable<StreamItem> streams) => streams
        .Select(stream => new
        {
            Stream = stream,
            Seeds = StreamSeederCount.Read(stream.AdditionalSources, stream.Description, stream.Title, stream.Name),
            Size = StreamFileSize.Read(stream)
        })
        .OrderBy(item => item.Stream.Resolution.Kind == StreamResolutionKind.NativeDirect ? 0
            : item.Stream.Resolution.Kind != StreamResolutionKind.RequiresStreamingService ? 4
            : item.Seeds is > 0 ? 1
            : item.Seeds is null ? 2 : 3)
        .ThenByDescending(item => item.Stream.Resolution.Kind == StreamResolutionKind.RequiresStreamingService
            ? item.Seeds : null)
        .ThenBy(item => item.Size ?? long.MaxValue)
        .Select(item => item.Stream);
}

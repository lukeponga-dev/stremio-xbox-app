using StremioXboxPrototype.Models;
using StremioXboxPrototype.Services;
using Windows.Storage;

var checks = 0;
var candidatesByReliability = new[]
{
    new StreamItem { Title = "Seeders: 0 100 MB", Resolution = StreamResolution.Service("torrent") },
    new StreamItem { Title = "Unknown 200 MB", Resolution = StreamResolution.Service("torrent") },
    new StreamItem { Title = "Seeders: 5 400 MB", Resolution = StreamResolution.Service("torrent") },
    new StreamItem { Title = "Seeders: 50 2 GB", Resolution = StreamResolution.Service("torrent") },
    new StreamItem { Title = "Seeders: 50 1 GB", Resolution = StreamResolution.Service("torrent") },
    new StreamItem { Title = "Direct 3 GB", Resolution = StreamResolution.Direct(new Uri("https://example.test/video.mp4")) }
};
Check(StreamPlaybackOrder.Order(candidatesByReliability).Select(s => s.Title).SequenceEqual(new[]
{
    "Direct 3 GB", "Seeders: 50 1 GB", "Seeders: 50 2 GB", "Seeders: 5 400 MB", "Unknown 200 MB", "Seeders: 0 100 MB"
}), "Playback ranking prefers direct sources, healthy swarms, then size; unknown peers precede zero peers");
Check(StreamPlaybackOrder.Order(Array.Empty<StreamItem>()).Count() == 0, "Empty stream list sorts safely");
Check(StreamFileSize.Read(new StreamItem { Description = "1080p 👤 400 💾 1.5 GB" }) == 1500000000, "Reads add-on file size without confusing seeders or resolution");
Check(StreamFileSize.Read(new StreamItem { Title = "900 MiB" }) == 943718400, "Converts binary file-size units");
Check(StreamFileSize.Read(new StreamItem { Title = "2 GB", BehaviorHints = new StreamBehaviorHints { VideoSize = 123 } }) == 123, "Structured size takes precedence");
Check(StreamFileSize.Read(new StreamItem { Title = "1080p 5.1" }) is null, "Missing sizes remain unknown");
var sizes = new[] { new StreamItem { Title = "Unknown" }, new StreamItem { Title = "2 GB" }, new StreamItem { Title = "900 MB" } };
Check(sizes.OrderBy(s => StreamFileSize.Read(s) ?? long.MaxValue).Select(s => s.Title).SequenceEqual(new[] { "900 MB", "2 GB", "Unknown" }), "Smallest streams precede larger and unknown sizes");
using (var guess = System.Text.Json.JsonDocument.Parse("{}"))
{
    var create = new StremioTorrentCreateRequest
    {
        Torrent = new StremioTorrentIdentity { InfoHash = new string('a', 40) },
        GuessFileIdx = guess.RootElement.Clone()
    };
    var json = System.Text.Json.JsonSerializer.Serialize(create, StremioJsonContext.Default.StremioTorrentCreateRequest);
    using var payload = System.Text.Json.JsonDocument.Parse(json);
    Check(payload.RootElement.GetProperty("torrent").GetProperty("infoHash").GetString() == new string('a', 40), "Torrent create includes the torrent identity");
    Check(payload.RootElement.GetProperty("guessFileIdx").ValueKind == System.Text.Json.JsonValueKind.Object, "Unknown file requests server selection");
    Check(!payload.RootElement.TryGetProperty("peerSearch", out _), "No trackers preserves server default discovery");
}
PrototypeSettings.SetStreamAddonText("https://saved.example/manifest.json");
var candidates = PrototypeSettings.ParseStreamAddons("https://candidate.example/manifest.json\nhttp://invalid.example/manifest.json\ninvalid");
Check(candidates.Count == 1 && candidates[0].ManifestUri.Host == "candidate.example", "Only HTTPS add-on candidates are parsed");
Check(PrototypeSettings.GetStreamAddons().Single().ManifestUri.Host == "saved.example", "Parsing unvalidated candidates preserves saved add-ons");
Check(PrototypeSettings.ParseStreamAddons(null).Count == 0, "Null candidate input is empty");
PrototypeSettings.SetStreamAddonText("");
Check(PrototypeSettings.GetStreamAddons().Count == 0, "Explicit removal clears configured add-ons");
var movie = new MetaItem { Id = "movie", Name = "A movie", Poster = "https://example.test/poster.jpg" };
Check(PrototypeSettings.GetWatchHistory().Count == 0, "Empty first-run history");
PrototypeSettings.SaveWatchProgress(movie, movie.Id, 120, 3600);
var saved = PrototypeSettings.GetWatchProgress(movie.Type, movie.Id, movie.Id)!;
Check(saved.PositionSeconds == 120 && saved.Item.Poster == movie.Poster, "Progress and artwork survive serialization");
Check(saved.RemainingText == "58 min left", "Remaining time label");
saved.Item.WatchProgress = saved;
PrototypeSettings.SaveWatchProgress(saved.Item, movie.Id, 180, 3600);
Check(PrototypeSettings.GetWatchHistory().Count == 1, "Updating a title replaces its history without a serialization cycle");
foreach (var position in new[] { 0d, 9d, double.NaN, double.PositiveInfinity })
    PrototypeSettings.SaveWatchProgress(movie, movie.Id, position, 3600);
Check(PrototypeSettings.GetWatchHistory()[0].PositionSeconds == 180, "Invalid and unopened playback preserve saved progress");
PrototypeSettings.SaveWatchProgress(movie, movie.Id, 3420, 3600);
Check(PrototypeSettings.GetWatchHistory().Count == 0, "95 percent completion removes title");
PrototypeSettings.SaveWatchProgress(movie, movie.Id, 100, 3600);
PrototypeSettings.SaveWatchProgress(movie, movie.Id, 0, 3600, completed: true);
Check(PrototypeSettings.GetWatchHistory().Count == 0, "MediaEnded removes progress even if the player resets position");
var series = new MetaItem { Id = "series", Type = "series", Name = "A series" };
PrototypeSettings.SaveWatchProgress(series, "series:1:2", 200, 1200);
Check(PrototypeSettings.GetWatchProgress("series", "series", "series:1:2")?.PositionSeconds == 200, "Resume matches episode identity");
Check(PrototypeSettings.GetWatchProgress("series", "series", "series:1:3") is null, "Another episode starts at zero");
PrototypeSettings.SaveWatchProgress(series, "series:1:3", 300, 1200);
Check(PrototypeSettings.GetWatchHistory().Single().VideoId == "series:1:3", "Shelf keeps the latest episode per series");
for (var index = 0; index < 15; index++)
    PrototypeSettings.SaveWatchProgress(new MetaItem { Id = $"title-{index}" }, $"title-{index}", 120, 1200);
Check(PrototypeSettings.GetWatchHistory().Count == 12, "History is bounded to twelve titles");
var values = ApplicationData.Current.LocalSettings.CreateContainer("WatchHistory", ApplicationDataCreateDisposition.Always).Values;
values["broken"] = "invalid JSON";
values["missing-title"] = "{\"Item\":null,\"PositionSeconds\":120,\"DurationSeconds\":1200}";
Check(PrototypeSettings.GetWatchHistory().Count == 12, "A malformed entry does not hide valid titles");
foreach (var viewport in new[] { 600d, 960, 1280, 1560, 1920, 2560, 3840 })
{
    var width = TvLayout.GetShelfCardWidth(viewport);
    Check(width >= 160 && width <= 200 && Math.Abs(width - Math.Clamp(viewport / 7.5, 160, 200)) < 0.001,
        $"Compact popular posters at {viewport}");
    var featuredWidth = TvLayout.GetContinueWatchingCardWidth(viewport);
    Check(featuredWidth >= 194 && featuredWidth <= 280 && featuredWidth > width,
        $"Continue Watching stays featured at {viewport}");
}
Check(TvLayout.GetShelfCardWidth(double.NaN) == 160 &&
      TvLayout.GetContinueWatchingCardWidth(double.NaN) == 194, "Safe sizing before layout");
foreach (var (width, height) in new[]
{
    (640d, 360d), (960d, 540d), (1280d, 720d), (1920d, 1080d),
    (2560d, 1440d), (3840d, 2160d), (7680d, 4320d), (2560d, 1080d)
})
{
    var layout = TvLayout.GetViewportLayout(width, height);
    var contentWidth = width - layout.SidebarWidth - layout.ContentGap - layout.HorizontalInset;
    Check(contentWidth > 360 && height - layout.VerticalInset * 2 > 0,
        $"Usable content fits {width} x {height}");
    Check(Math.Abs(layout.HorizontalInset - width * 0.05) < 0.001 &&
        Math.Abs(layout.VerticalInset - height * 0.05) < 0.001,
        $"Controls retain five-percent safe spacing at {width} x {height}");
    Check(layout.SidebarWidth == 80 &&
          layout.ContentGap == (width < 1000 ? 24 : 48),
        $"Reference rail and safe content gap at {width} x {height}");
}
var referenceLayout = TvLayout.GetViewportLayout(1294, 1024);
var referenceContentWidth = 1294 - referenceLayout.SidebarWidth - referenceLayout.ContentGap - referenceLayout.HorizontalInset;
Check(referenceLayout.SidebarWidth + referenceLayout.ContentGap == 128 &&
      TvLayout.GetShelfCardWidth(referenceContentWidth) == 160 &&
      TvLayout.GetContinueWatchingCardWidth(referenceContentWidth) == 194,
    "1294px reference proportions for rail and poster shelves");
var xboxLayout = TvLayout.GetViewportLayout(960, 540);
Check(xboxLayout.StackHeader && !xboxLayout.ShowDetailsPoster,
    "Xbox effective viewport gives header actions and details adequate space");
var desktopLayout = TvLayout.GetViewportLayout(1920, 1080);
Check(!desktopLayout.StackHeader && desktopLayout.ShowDetailsPoster,
    "Wide viewport restores side-by-side layout");
Check(TvLayout.GetViewportLayout(double.NaN, 0) == xboxLayout,
    "Unmeasured viewport uses a safe Xbox-sized fallback");
foreach (var (width, columns, stackTop, stackImport) in new[]
{
    (1090d, 3, false, false), (600d, 3, false, false),
    (430d, 2, false, true), (359d, 1, true, true), (150d, 1, true, true)
})
{
    var grid = TvLayout.GetAddonGridLayout(width);
    Check(grid.Columns == columns && grid.StackTopActions == stackTop &&
          grid.StackImportActions == stackImport && grid.GridWidth <= width &&
          grid.Columns * grid.TileWidth <= grid.GridWidth,
        $"Addon tiles and actions fit {width}px");
}
Console.WriteLine($"All {checks} watch-history and TV-layout checks passed.");

void Check(bool condition, string label)
{
    if (!condition) throw new Exception(label);
    checks++;
}

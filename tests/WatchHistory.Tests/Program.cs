using StremioXboxPrototype.Models;
using StremioXboxPrototype.Services;
using Windows.Storage;

var checks = 0;
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
    var count = Math.Floor(viewport / width);
    Check(width >= 240 && Math.Abs(viewport / width - count - 0.25) < 0.001, $"Readable cards and next-card cue at {viewport}");
}
Check(TvLayout.GetShelfCardWidth(double.NaN) == 240, "Safe sizing before layout");
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
    Check(Math.Abs(layout.SidebarWidth - layout.HorizontalInset - 8 - 168) < 0.001,
        $"Navigation fits inside sidebar at {width} x {height}");
}
var xboxLayout = TvLayout.GetViewportLayout(960, 540);
Check(xboxLayout.StackHeader && !xboxLayout.ShowDetailsPoster,
    "Xbox effective viewport gives header actions and details adequate space");
var desktopLayout = TvLayout.GetViewportLayout(1920, 1080);
Check(!desktopLayout.StackHeader && desktopLayout.ShowDetailsPoster,
    "Wide viewport restores side-by-side layout");
Check(TvLayout.GetViewportLayout(double.NaN, 0) == xboxLayout,
    "Unmeasured viewport uses a safe Xbox-sized fallback");
Console.WriteLine($"All {checks} watch-history and TV-layout checks passed.");

void Check(bool condition, string label)
{
    if (!condition) throw new Exception(label);
    checks++;
}

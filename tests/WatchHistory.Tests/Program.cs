using StremioXboxPrototype.Models;
using StremioXboxPrototype.Services;
using Windows.Storage;

var checks = 0;
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
Console.WriteLine($"All {checks} watch-history and TV-layout checks passed.");

void Check(bool condition, string label)
{
    if (!condition) throw new Exception(label);
    checks++;
}

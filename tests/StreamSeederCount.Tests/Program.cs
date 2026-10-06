using System.Text.Json;
using StremioXboxPrototype.Services;

var cases = new (string Label, long? Expected)[]
{
    ("Movie 2026 1080p 5.1 2GB", null),
    ("👤 123 💾 2.5 GB", 123),
    ("👥 1,234", 1234),
    ("Seeders: 0", 0),
    ("SEEDERS = 1.2k", 1200),
    ("250 seeds", 250),
    ("Seeders: unknown", null),
    ("Seeders: -3", null),
    ("Seeders: 9999999999999999999999999999999999999", null)
};
foreach (var (label, expected) in cases)
    Check(label, expected, StreamSeederCount.Read(null, label));

foreach (var json in new[] { "{\"seeders\":42}", "{\"seeders\":\"42\"}", "{\"seeds\":42}" })
    Check(json, 42, StreamSeederCount.Read(JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json), "👤 999"));
Check("invalid structured count falls back", 12,
    StreamSeederCount.Read(JsonSerializer.Deserialize<Dictionary<string, JsonElement>>("{\"seeders\":-1}"), "👤 12"));
Check("fallback across labels", 80, StreamSeederCount.Read(null, "Movie 1080p", "Seeders: 80"));
Console.WriteLine("All 14 seeder-count checks passed.");

static void Check(string label, long? expected, long? actual)
{
    if (actual != expected) throw new Exception($"{label}: expected {expected}, got {actual}");
}

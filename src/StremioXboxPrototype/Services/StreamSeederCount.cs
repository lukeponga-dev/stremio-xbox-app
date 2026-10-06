using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace StremioXboxPrototype.Services;

public static class StreamSeederCount
{
    // Providers may supply structured counts or include them in their display text.
    private const string CountPattern = @"(?<count>\d+(?:,\d{3})*(?:\.\d+)?\s*[kKmM]?)(?![\d.])";
    private static readonly Regex LabeledCount = new(
        @"(?:\bseeders?\b|\bseeds\b|👤|👥)\s*[:=]?\s*" + CountPattern,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex CountBeforeLabel = new(
        @"(?<![\w.])" + CountPattern + @"\s+(?:seeders?|seeds)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public static long? Read(Dictionary<string, JsonElement>? fields, params string?[] labels)
    {
        foreach (var key in new[] { "seeders", "seeds", "seedersCount", "seederCount" })
        {
            if (fields is null || !fields.TryGetValue(key, out var value)) continue;
            var count = Parse(value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString());
            if (count.HasValue) return count;
        }

        foreach (var label in labels)
        {
            if (string.IsNullOrWhiteSpace(label)) continue;
            try
            {
                var match = LabeledCount.Match(label);
                if (!match.Success) match = CountBeforeLabel.Match(label);
                if (match.Success && Parse(match.Groups["count"].Value) is { } count) return count;
            }
            catch (RegexMatchTimeoutException) { }
        }
        return null;
    }

    private static long? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim();
        var multiplier = char.ToLowerInvariant(text[^1]) switch { 'k' => 1000m, 'm' => 1000000m, _ => 1m };
        if (multiplier != 1m) text = text[..^1].TrimEnd();
        text = text.Replace(",", "");
        if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number) ||
            number < 0 || number > long.MaxValue / multiplier) return null;
        return (long)(number * multiplier);
    }
}

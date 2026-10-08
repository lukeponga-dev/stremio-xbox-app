using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using StremioXboxPrototype.Models;

namespace StremioXboxPrototype.Services;

public static class StreamFileSize
{
    private static readonly Regex SizeLabel = new(
        @"(?<![\w.,-])(?<size>\d+(?:\.\d+)?)\s*(?<unit>[KMGT]i?B)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    public static long? Read(StreamItem stream)
    {
        if (stream.BehaviorHints?.VideoSize is > 0) return stream.BehaviorHints.VideoSize;
        if (stream.AdditionalSources is not null)
        {
            foreach (var key in new[] { "videoSize", "fileSize", "size", "bytes" })
            {
                if (!stream.AdditionalSources.TryGetValue(key, out var value)) continue;
                if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var bytes) && bytes > 0)
                    return bytes;
                if (value.ValueKind == JsonValueKind.String)
                {
                    var text = value.GetString();
                    if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out bytes) && bytes > 0)
                        return bytes;
                    if (ParseLabel(text) is long parsed) return parsed;
                }
            }
        }
        return ParseLabel(stream.Description) ?? ParseLabel(stream.Title) ?? ParseLabel(stream.Name);
    }

    private static long? ParseLabel(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        foreach (Match match in SizeLabel.Matches(text))
        {
            if (!double.TryParse(match.Groups["size"].Value, NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out var size)) continue;
            var unit = match.Groups["unit"].Value.ToUpperInvariant();
            var power = "KMGT".IndexOf(unit[0]) + 1;
            var bytes = size * Math.Pow(unit.Contains('I') ? 1024 : 1000, power);
            if (double.IsFinite(bytes) && bytes >= 1 && bytes < long.MaxValue) return (long)bytes;
        }
        return null;
    }
}

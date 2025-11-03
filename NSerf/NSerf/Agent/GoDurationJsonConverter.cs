// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace NSerf.Agent;

/// <summary>
/// JSON converter for Go duration format (e.g., "15 s", "48 h", "30 m")
/// Maps to Go's time.ParseDuration
/// </summary>
public partial class GoDurationJsonConverter : JsonConverter<TimeSpan>
{
    public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            // Handle numeric values as seconds (fallback)
            return TimeSpan.FromSeconds(reader.GetDouble());
        }

        var value = reader.GetString();
        return string.IsNullOrEmpty(value) ? TimeSpan.Zero : ParseGoDuration(value);
    }

    public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(FormatGoDuration(value));
    }

    [GeneratedRegex(@"^(\d+(?:\.\d+)?)(ns|us|µs|ms|s|m|h)$", RegexOptions.CultureInvariant)]
    private static partial Regex DurationRegex();

    private static TimeSpan ParseGoDuration(string input)
    {
        // Support Go duration format: h (hour), m (minute), s (second), ms (millisecond), us (microsecond), ns (nanosecond)
        var match = DurationRegex().Match(input);
        if (!match.Success)
            throw new FormatException($"Invalid duration format: {input}");

        var value = double.Parse(match.Groups[1].Value);
        var unit = match.Groups[2].Value;

        return unit switch
        {
            "h" => TimeSpan.FromHours(value),
            "m" => TimeSpan.FromMinutes(value),
            "s" => TimeSpan.FromSeconds(value),
            "ms" => TimeSpan.FromMilliseconds(value),
            "us" or "µs" => TimeSpan.FromMicroseconds(value),
            "ns" => TimeSpan.FromTicks((long)(value / 100)),  // 100ns = 1 tick
            _ => throw new FormatException($"Unsupported duration unit: {unit}")
        };
    }

    private static string FormatGoDuration(TimeSpan value)
    {
        return value switch
        {
            { TotalHours: >= 1 } when value.Ticks % TimeSpan.TicksPerHour == 0
                => $"{(int)value.TotalHours}h",
            { TotalMinutes: >= 1 } when value.Ticks % TimeSpan.TicksPerMinute == 0
                => $"{(int)value.TotalMinutes}m",
            { TotalSeconds: >= 1 } when value.Ticks % TimeSpan.TicksPerSecond == 0
                => $"{(int)value.TotalSeconds}s",
            { TotalMilliseconds: >= 1 }
                => $"{(int)value.TotalMilliseconds}ms",
            _
                => $"{value.Ticks * 100}ns"
        };
    }
}

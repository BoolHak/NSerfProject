// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace NSerf.Agent;

/// <summary>
/// JSON converter for Go duration format (e.g., "15s", "48h", "30m")
/// Maps to Go's time.ParseDuration
/// </summary>
public class GoDurationJsonConverter : JsonConverter<TimeSpan>
{
    public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            // Handle numeric values as seconds (fallback)
            return TimeSpan.FromSeconds(reader.GetDouble());
        }
        
        var value = reader.GetString();
        if (string.IsNullOrEmpty(value))
            return TimeSpan.Zero;
        
        return ParseGoDuration(value);
    }

    public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(FormatGoDuration(value));
    }

    public static TimeSpan ParseGoDuration(string input)
    {
        // Support Go duration format: h (hour), m (minute), s (second), ms (millisecond), us (microsecond), ns (nanosecond)
        var match = Regex.Match(input, @"^(\d+(?:\.\d+)?)(ns|us|µs|ms|s|m|h)$");
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
        if (value.TotalHours >= 1 && value.TotalHours == Math.Floor(value.TotalHours))
            return $"{(int)value.TotalHours}h";
        if (value.TotalMinutes >= 1 && value.TotalMinutes == Math.Floor(value.TotalMinutes))
            return $"{(int)value.TotalMinutes}m";
        if (value.TotalSeconds >= 1 && value.TotalSeconds == Math.Floor(value.TotalSeconds))
            return $"{(int)value.TotalSeconds}s";
        if (value.TotalMilliseconds >= 1)
            return $"{(int)value.TotalMilliseconds}ms";
        
        return $"{value.Ticks * 100}ns";
    }
}

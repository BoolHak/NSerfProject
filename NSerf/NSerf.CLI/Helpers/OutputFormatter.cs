// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.Text.Json;
using NSerf.Client.Responses;

namespace NSerf.CLI.Helpers;

/// <summary>
/// Helper for formatting command output in different formats.
/// </summary>
public static class OutputFormatter
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        WriteIndented = true
    };
    
    public enum OutputFormat
    {
        Text,
        Json
    }

    /// <summary>
    /// Parses output format from string.
    /// </summary>
    public static OutputFormat ParseFormat(string? format)
    {
        if (string.IsNullOrEmpty(format))
            return OutputFormat.Text;

        return format.ToLowerInvariant() switch
        {
            "json" => OutputFormat.Json,
            "text" => OutputFormat.Text,
            _ => throw new ArgumentException($"Invalid format: {format}. Valid formats are 'text' or 'json'.")
        };
    }

    /// <summary>
    /// Formats members list output.
    /// </summary>
    public static void FormatMembers(
        Member[] members,
        OutputFormat format,
        bool detailed = false)
    {
        if (format == OutputFormat.Json)
        {
            // Convert members to JSON-friendly format (Addr is a byte[] which needs conversion)
            var jsonMembers = members.Select(m => new
            {
                name = m.Name,
                addr = m.Addr.Length > 0 ? new System.Net.IPAddress(m.Addr).ToString() : "",
                port = m.Port,
                tags = m.Tags,
                status = m.Status,
                protocol_min = m.ProtocolMin,
                protocol_max = m.ProtocolMax,
                protocol_cur = m.ProtocolCur,
                delegate_min = m.DelegateMin,
                delegate_max = m.DelegateMax,
                delegate_cur = m.DelegateCur
            }).ToArray();
            
            var json = JsonSerializer.Serialize(new { members = jsonMembers }, JsonSerializerOptions);
            Console.WriteLine(json);
            return;
        }

        // Text format - table
        if (members.Length == 0)
        {
            Console.WriteLine("No members found.");
            return;
        }

        // Calculate column widths
        var nameWidth = Math.Max(20, members.Max(m => m.Name.Length) + 2);
        var addrWidth = Math.Max(20, members.Max(m =>
        {
            var ipAddr = m.Addr.Length > 0 ? new System.Net.IPAddress(m.Addr).ToString() : "";
            return $"{ipAddr}:{m.Port}".Length;
        }) + 2);
        const int statusWidth = 10;

        // Header
        Console.WriteLine($"{"Name".PadRight(nameWidth)}{"Address".PadRight(addrWidth)}{"Status",-statusWidth}Tags");
        Console.WriteLine(new string('-', nameWidth + addrWidth + statusWidth + 40));

        // Rows
        foreach (var member in members)
        {
            var name = member.Name.PadRight(nameWidth);
            var ipAddr = member.Addr.Length > 0 ? new System.Net.IPAddress(member.Addr).ToString() : "";
            var addr = $"{ipAddr}:{member.Port}".PadRight(addrWidth);
            var status = member.Status.PadRight(statusWidth);
            var tags = string.Join(",", member.Tags.Select(t => $"{t.Key}={t.Value}"));

            Console.Write(name);
            Console.Write(addr);
            Console.Write(status);
            Console.WriteLine(tags);

            if (detailed)
            {
                Console.WriteLine($"  Protocol: {member.ProtocolCur}, " +
                                $"Range: [{member.ProtocolMin}, {member.ProtocolMax}]");
            }
        }
    }

    /// <summary>
    /// Formats stats output.
    /// </summary>
    public static void FormatStats(StatsResponse stats, OutputFormat format)
    {
        if (format == OutputFormat.Json)
        {
            var json = JsonSerializer.Serialize(stats, JsonSerializerOptions);
            Console.WriteLine(json);
            return;
        }

        // Text format
        Console.WriteLine("Agent Stats:");
        foreach (var kvp in stats.Stats.OrderBy(s => s.Key))
        {
            Console.WriteLine($"  {kvp.Key}: {kvp.Value}");
        }
    }

    /// <summary>
    /// Formats a generic object as JSON.
    /// </summary>
    public static void FormatJson<T>(T obj)
    {
        var json = JsonSerializer.Serialize(obj, JsonSerializerOptions);
        Console.WriteLine(json);
    }
}

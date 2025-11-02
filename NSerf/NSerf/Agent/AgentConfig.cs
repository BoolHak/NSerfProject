// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.Text.Json.Serialization;

namespace NSerf.Agent;

public class AgentConfig
{
    // Basic Configuration
    public string NodeName { get; set; } = string.Empty;
    public string? Role { get; set; }  // Deprecated - use Tags["role"]
    public string BindAddr { get; set; } = "0.0.0.0:7946";
    [JsonPropertyName("advertise")]
    public string? AdvertiseAddr { get; set; }
    public string? EncryptKey { get; set; }  // Base64-encoded 32-byte key
    public string? KeyringFile { get; set; }
    public string LogLevel { get; set; } = "INFO";
    public string? RPCAddr { get; set; }  // Changed to empty default
    public string? RPCAuthKey { get; set; }
    public Dictionary<string, string> Tags { get; set; } = [];
    public string? TagsFile { get; set; }
    public string Profile { get; set; } = "lan";  // lan, wan, or local
    public string? SnapshotPath { get; set; }
    public int Protocol { get; set; } = 5;

    // Rejoin/Retry Configuration
    public bool RejoinAfterLeave { get; set; }
    public bool ReplayOnJoin { get; set; } = false;
    public string[] StartJoin { get; set; } = [];
    public string[] StartJoinWan { get; set; } = [];
    public string[] RetryJoin { get; set; } = [];
    public TimeSpan RetryInterval { get; set; } = TimeSpan.FromSeconds(30);
    public int RetryMaxAttempts { get; set; }
    public string[] RetryJoinWan { get; set; } = [];
    public TimeSpan RetryIntervalWan { get; set; } = TimeSpan.FromSeconds(30);
    public int RetryMaxAttemptsWan { get; set; }

    // Memberlist Timeouts
    public TimeSpan ReconnectInterval { get; set; } = TimeSpan.FromSeconds(60);
    public TimeSpan ReconnectTimeout { get; set; } = TimeSpan.FromHours(72);
    public TimeSpan TombstoneTimeout { get; set; } = TimeSpan.FromHours(24);
    public TimeSpan BroadcastTimeout { get; set; } = TimeSpan.FromSeconds(5);

    // Features
    public bool DisableCoordinates { get; set; }
    public bool DisableNameResolution { get; set; }
    public bool EnableCompression { get; set; }
    public bool LeaveOnTerm { get; set; } = true;
    public bool SkipLeaveOnInt { get; set; }

    // Logging
    public bool EnableSyslog { get; set; }
    public string SyslogFacility { get; set; } = "LOCAL0";

    // Discovery
    public string? Interface { get; set; }
    public string? Discover { get; set; }  // mDNS discovery

    // Event Handlers
    public List<string> EventHandlers { get; set; } = [];

    // Metrics
    public string? StatsiteAddr { get; set; }
    public string? StatsdAddr { get; set; }

    // Limits
    public int UserEventSizeLimit { get; set; } = 512;

    // Default port constant
    public const int DefaultBindPort = 7946;

    public static AgentConfig Default()
    {
        return new AgentConfig();
    }

    public static AgentConfig Merge(AgentConfig a, AgentConfig b)
    {
        var result = new AgentConfig();

        MergeScalars(a, b, result);
        MergeIntegers(a, b, result);
        MergeTimeSpans(a, b, result);
        MergeBooleans(a, b, result);
        MergeArrays(a, b, result);

        // Tags - MERGE (later value wins for same key)
        result.Tags = new Dictionary<string, string>(a.Tags);
        foreach (var kvp in b.Tags)
        {
            result.Tags[kvp.Key] = kvp.Value;
        }

        // If Role is set, ensure it's in Tags
        if (!string.IsNullOrEmpty(result.Role) && !result.Tags.ContainsKey("role"))
        {
            result.Tags["role"] = result.Role;
        }

        return result;
    }

    private static void MergeArrays(AgentConfig a, AgentConfig b, AgentConfig result)
    {
        result.StartJoin = [.. a.StartJoin, .. b.StartJoin];
        result.StartJoinWan = [.. a.StartJoinWan, .. b.StartJoinWan];
        result.RetryJoin = [.. a.RetryJoin, .. b.RetryJoin];
        result.RetryJoinWan = [.. a.RetryJoinWan, .. b.RetryJoinWan];
        result.EventHandlers = [.. a.EventHandlers, .. b.EventHandlers];
    }

    private static void MergeBooleans(AgentConfig a, AgentConfig b, AgentConfig result)
    {
        result.RejoinAfterLeave = b.RejoinAfterLeave || a.RejoinAfterLeave;
        result.DisableCoordinates = b.DisableCoordinates || a.DisableCoordinates;
        result.DisableNameResolution = b.DisableNameResolution || a.DisableNameResolution;
        result.EnableCompression = b.EnableCompression || a.EnableCompression;
        result.LeaveOnTerm = b.LeaveOnTerm && a.LeaveOnTerm;  // Both must be true
        result.SkipLeaveOnInt = b.SkipLeaveOnInt || a.SkipLeaveOnInt;
        result.EnableSyslog = b.EnableSyslog || a.EnableSyslog;
    }

    private static void MergeTimeSpans(AgentConfig a, AgentConfig b, AgentConfig result)
    {
        result.RetryInterval = b.RetryInterval != TimeSpan.Zero ? b.RetryInterval : a.RetryInterval;
        result.RetryIntervalWan = b.RetryIntervalWan != TimeSpan.Zero ? b.RetryIntervalWan : a.RetryIntervalWan;
        result.ReconnectInterval = b.ReconnectInterval != TimeSpan.Zero ? b.ReconnectInterval : a.ReconnectInterval;
        result.ReconnectTimeout = b.ReconnectTimeout != TimeSpan.Zero ? b.ReconnectTimeout : a.ReconnectTimeout;
        result.TombstoneTimeout = b.TombstoneTimeout != TimeSpan.Zero ? b.TombstoneTimeout : a.TombstoneTimeout;
        result.BroadcastTimeout = b.BroadcastTimeout != TimeSpan.Zero ? b.BroadcastTimeout : a.BroadcastTimeout;
    }

    private static void MergeIntegers(AgentConfig a, AgentConfig b, AgentConfig result)
    {
        result.RetryMaxAttempts = b.RetryMaxAttempts != 0 ? b.RetryMaxAttempts : a.RetryMaxAttempts;
        result.RetryMaxAttemptsWan = b.RetryMaxAttemptsWan != 0 ? b.RetryMaxAttemptsWan : a.RetryMaxAttemptsWan;
        result.UserEventSizeLimit = b.UserEventSizeLimit != 0 ? b.UserEventSizeLimit : a.UserEventSizeLimit;
    }

    private static void MergeScalars(AgentConfig a, AgentConfig b, AgentConfig result)
    {
        result.NodeName = GetValueOrDefault(a, b, c => c.NodeName);
        result.Role = GetValueOrDefault(a, b, c => c.Role);
        result.BindAddr = GetValueOrDefault(a, b, c => c.BindAddr);
        result.AdvertiseAddr = GetValueOrDefault(a, b, c => c.AdvertiseAddr);
        result.EncryptKey = GetValueOrDefault(a, b, c => c.EncryptKey);
        result.KeyringFile = GetValueOrDefault(a, b, c => c.KeyringFile);
        result.LogLevel = GetValueOrDefault(a, b, c => c.LogLevel);
        result.RPCAddr = GetValueOrDefault(a, b, c => c.RPCAddr);
        result.RPCAuthKey = GetValueOrDefault(a, b, c => c.RPCAuthKey);
        result.TagsFile = GetValueOrDefault(a, b, c => c.TagsFile);
        result.Profile = GetValueOrDefault(a, b, c => c.Profile);
        result.SnapshotPath = GetValueOrDefault(a, b, c => c.SnapshotPath);
        result.SyslogFacility = GetValueOrDefault(a, b, c => c.SyslogFacility);
        result.Interface = GetValueOrDefault(a, b, c => c.Interface);
        result.Discover = GetValueOrDefault(a, b, c => c.Discover);
        result.StatsiteAddr = GetValueOrDefault(a, b, c => c.StatsiteAddr);
        result.StatsdAddr = GetValueOrDefault(a, b, c => c.StatsdAddr);

        result.Protocol = b.Protocol != 0 ? b.Protocol : a.Protocol;
    }

    private static string GetValueOrDefault(AgentConfig a, AgentConfig b, Func<AgentConfig, string?> getValue)
    {
        var bVal = getValue(b);
        if (!string.IsNullOrEmpty(bVal))
            return bVal;

        var aVal = getValue(a);
        return aVal ?? string.Empty;
    }

    public (string IP, int Port) AddrParts(string? address = null)
    {
        address ??= BindAddr;

        if (string.IsNullOrEmpty(address))
            return ("0.0.0.0", DefaultBindPort);

        if (!address.Contains(':'))
            return (address, DefaultBindPort);

        var parts = address.Split(':');
        var ip = string.IsNullOrEmpty(parts[0]) ? "0.0.0.0" : parts[0];
        var port = int.Parse(parts[1]);

        return (ip, port);
    }

    public byte[]? EncryptBytes()
    {
        if (string.IsNullOrEmpty(EncryptKey))
            return null;

        var bytes = Convert.FromBase64String(EncryptKey);

        if (bytes.Length != 32)
            throw new ConfigException("Encrypt key must be exactly 32 bytes");

        return bytes;
    }
}

public class EventScriptConfig
{
    public string Event { get; set; } = string.Empty;
    public string Script { get; set; } = string.Empty;
}

public class ConfigException : Exception
{
    public ConfigException(string message) : base(message) { }
    public ConfigException(string message, Exception innerException) : base(message, innerException) { }
}

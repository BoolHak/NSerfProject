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
    public Dictionary<string, string> Tags { get; set; } = new();
    public string? TagsFile { get; set; }
    public string Profile { get; set; } = "lan";  // lan, wan, or local
    public string? SnapshotPath { get; set; }
    public int Protocol { get; set; } = 5;
    
    // Rejoin/Retry Configuration
    public bool RejoinAfterLeave { get; set; }
    public bool ReplayOnJoin { get; set; } = false;
    public string[] StartJoin { get; set; } = Array.Empty<string>();
    public string[] StartJoinWan { get; set; } = Array.Empty<string>();
    public string[] RetryJoin { get; set; } = Array.Empty<string>();
    public TimeSpan RetryInterval { get; set; } = TimeSpan.FromSeconds(30);
    public int RetryMaxAttempts { get; set; }
    public string[] RetryJoinWan { get; set; } = Array.Empty<string>();
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
    public List<string> EventHandlers { get; set; } = new();
    
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
        
        // Scalars - later wins
        result.NodeName = !string.IsNullOrEmpty(b.NodeName) ? b.NodeName : a.NodeName;
        result.Role = !string.IsNullOrEmpty(b.Role) ? b.Role : a.Role;
        result.BindAddr = !string.IsNullOrEmpty(b.BindAddr) ? b.BindAddr : a.BindAddr;
        result.AdvertiseAddr = !string.IsNullOrEmpty(b.AdvertiseAddr) ? b.AdvertiseAddr : a.AdvertiseAddr;
        result.EncryptKey = !string.IsNullOrEmpty(b.EncryptKey) ? b.EncryptKey : a.EncryptKey;
        result.KeyringFile = !string.IsNullOrEmpty(b.KeyringFile) ? b.KeyringFile : a.KeyringFile;
        result.LogLevel = !string.IsNullOrEmpty(b.LogLevel) ? b.LogLevel : a.LogLevel;
        result.RPCAddr = !string.IsNullOrEmpty(b.RPCAddr) ? b.RPCAddr : a.RPCAddr;
        result.RPCAuthKey = !string.IsNullOrEmpty(b.RPCAuthKey) ? b.RPCAuthKey : a.RPCAuthKey;
        result.TagsFile = !string.IsNullOrEmpty(b.TagsFile) ? b.TagsFile : a.TagsFile;
        result.Profile = !string.IsNullOrEmpty(b.Profile) ? b.Profile : a.Profile;
        result.SnapshotPath = !string.IsNullOrEmpty(b.SnapshotPath) ? b.SnapshotPath : a.SnapshotPath;
        result.Protocol = b.Protocol != 0 ? b.Protocol : a.Protocol;
        result.SyslogFacility = !string.IsNullOrEmpty(b.SyslogFacility) ? b.SyslogFacility : a.SyslogFacility;
        result.Interface = !string.IsNullOrEmpty(b.Interface) ? b.Interface : a.Interface;
        result.Discover = !string.IsNullOrEmpty(b.Discover) ? b.Discover : a.Discover;
        result.StatsiteAddr = !string.IsNullOrEmpty(b.StatsiteAddr) ? b.StatsiteAddr : a.StatsiteAddr;
        result.StatsdAddr = !string.IsNullOrEmpty(b.StatsdAddr) ? b.StatsdAddr : a.StatsdAddr;
        
        // Integers - later wins if non-zero
        result.RetryMaxAttempts = b.RetryMaxAttempts != 0 ? b.RetryMaxAttempts : a.RetryMaxAttempts;
        result.RetryMaxAttemptsWan = b.RetryMaxAttemptsWan != 0 ? b.RetryMaxAttemptsWan : a.RetryMaxAttemptsWan;
        result.UserEventSizeLimit = b.UserEventSizeLimit != 0 ? b.UserEventSizeLimit : a.UserEventSizeLimit;
        
        // TimeSpans - later wins if not zero
        result.RetryInterval = b.RetryInterval != TimeSpan.Zero ? b.RetryInterval : a.RetryInterval;
        result.RetryIntervalWan = b.RetryIntervalWan != TimeSpan.Zero ? b.RetryIntervalWan : a.RetryIntervalWan;
        result.ReconnectInterval = b.ReconnectInterval != TimeSpan.Zero ? b.ReconnectInterval : a.ReconnectInterval;
        result.ReconnectTimeout = b.ReconnectTimeout != TimeSpan.Zero ? b.ReconnectTimeout : a.ReconnectTimeout;
        result.TombstoneTimeout = b.TombstoneTimeout != TimeSpan.Zero ? b.TombstoneTimeout : a.TombstoneTimeout;
        result.BroadcastTimeout = b.BroadcastTimeout != TimeSpan.Zero ? b.BroadcastTimeout : a.BroadcastTimeout;
        
        // Booleans - special handling
        result.RejoinAfterLeave = b.RejoinAfterLeave || a.RejoinAfterLeave;
        result.DisableCoordinates = b.DisableCoordinates || a.DisableCoordinates;
        result.DisableNameResolution = b.DisableNameResolution || a.DisableNameResolution;
        result.EnableCompression = b.EnableCompression || a.EnableCompression;
        result.LeaveOnTerm = b.LeaveOnTerm && a.LeaveOnTerm;  // Both must be true
        result.SkipLeaveOnInt = b.SkipLeaveOnInt || a.SkipLeaveOnInt;
        result.EnableSyslog = b.EnableSyslog || a.EnableSyslog;
        
        // Arrays - APPEND (not replace)
        result.StartJoin = a.StartJoin.Concat(b.StartJoin).ToArray();
        result.StartJoinWan = a.StartJoinWan.Concat(b.StartJoinWan).ToArray();
        result.RetryJoin = a.RetryJoin.Concat(b.RetryJoin).ToArray();
        result.RetryJoinWan = a.RetryJoinWan.Concat(b.RetryJoinWan).ToArray();
        result.EventHandlers = a.EventHandlers.Concat(b.EventHandlers).ToList();
        
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

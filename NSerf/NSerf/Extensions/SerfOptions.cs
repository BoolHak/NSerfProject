// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

namespace NSerf.Extensions;

/// <summary>
/// Configuration options for Serf agent when using dependency injection.
/// </summary>
public class SerfOptions
{
    /// <summary>
    /// The name of this node. Must be unique in the cluster.
    /// </summary>
    public string NodeName { get; set; } = Environment.MachineName;

    /// <summary>
    /// Bind address in "IP:Port" format. Default: "0.0.0.0:7946"
    /// </summary>
    public string BindAddr { get; set; } = "0.0.0.0:7946";

    /// <summary>
    /// Advertise address for NAT traversal (optional).
    /// </summary>
    public string? AdvertiseAddr { get; set; }

    /// <summary>
    /// Base64-encoded 32-byte encryption key (optional).
    /// </summary>
    public string? EncryptKey { get; set; }

    /// <summary>
    /// Path to keyring file for encryption (optional).
    /// </summary>
    public string? KeyringFile { get; set; }

    /// <summary>
    /// RPC server address in "IP:Port" format (optional).
    /// </summary>
    public string? RPCAddr { get; set; }

    /// <summary>
    /// RPC authentication key (optional).
    /// </summary>
    public string? RPCAuthKey { get; set; }

    /// <summary>
    /// Node tags for metadata.
    /// </summary>
    public Dictionary<string, string> Tags { get; set; } = new();

    /// <summary>
    /// Path to tags file (optional).
    /// </summary>
    public string? TagsFile { get; set; }

    /// <summary>
    /// Network profile: "lan", "wan", or "local". Default: "lan"
    /// </summary>
    public string Profile { get; set; } = "lan";

    /// <summary>
    /// Path to snapshot file for state persistence (optional).
    /// </summary>
    public string? SnapshotPath { get; set; }

    /// <summary>
    /// Serf protocol version. Default: 5
    /// </summary>
    public int Protocol { get; set; } = 5;

    /// <summary>
    /// Allow rejoining after graceful leave. Default: false
    /// </summary>
    public bool RejoinAfterLeave { get; set; }

    /// <summary>
    /// Replay old events when joining. Default: false
    /// </summary>
    public bool ReplayOnJoin { get; set; }

    /// <summary>
    /// Nodes to join on startup.
    /// </summary>
    public string[] StartJoin { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Nodes to retry joining in background.
    /// </summary>
    public string[] RetryJoin { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Retry join interval. Default: 30 seconds
    /// </summary>
    public TimeSpan RetryInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Maximum retry join attempts (0 = unlimited). Default: 0
    /// </summary>
    public int RetryMaxAttempts { get; set; }

    /// <summary>
    /// Disable network coordinates. Default: false
    /// </summary>
    public bool DisableCoordinates { get; set; }

    /// <summary>
    /// Event handler scripts in format "type=script" or "type:filter=script".
    /// </summary>
    public List<string> EventHandlers { get; set; } = new();

    /// <summary>
    /// Reconnect interval for failed nodes. Default: 60 seconds
    /// </summary>
    public TimeSpan ReconnectInterval { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Reconnect timeout before marking as permanently failed. Default: 72 hours
    /// </summary>
    public TimeSpan ReconnectTimeout { get; set; } = TimeSpan.FromHours(72);

    /// <summary>
    /// Tombstone timeout for left nodes. Default: 24 hours
    /// </summary>
    public TimeSpan TombstoneTimeout { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Leave gracefully on SIGTERM. Default: true
    /// </summary>
    public bool LeaveOnTerm { get; set; } = true;

    /// <summary>
    /// Skip leave on SIGINT. Default: false
    /// </summary>
    public bool SkipLeaveOnInt { get; set; }

    /// <summary>
    /// Converts SerfOptions to AgentConfig.
    /// </summary>
    internal Agent.AgentConfig ToAgentConfig()
    {
        return new Agent.AgentConfig
        {
            NodeName = NodeName,
            BindAddr = BindAddr,
            AdvertiseAddr = AdvertiseAddr,
            EncryptKey = EncryptKey,
            KeyringFile = KeyringFile,
            RPCAddr = RPCAddr,
            RPCAuthKey = RPCAuthKey,
            Tags = new Dictionary<string, string>(Tags),
            TagsFile = TagsFile,
            Profile = Profile,
            SnapshotPath = SnapshotPath,
            Protocol = Protocol,
            RejoinAfterLeave = RejoinAfterLeave,
            ReplayOnJoin = ReplayOnJoin,
            StartJoin = StartJoin,
            RetryJoin = RetryJoin,
            RetryInterval = RetryInterval,
            RetryMaxAttempts = RetryMaxAttempts,
            DisableCoordinates = DisableCoordinates,
            EventHandlers = new List<string>(EventHandlers),
            ReconnectInterval = ReconnectInterval,
            ReconnectTimeout = ReconnectTimeout,
            TombstoneTimeout = TombstoneTimeout,
            LeaveOnTerm = LeaveOnTerm,
            SkipLeaveOnInt = SkipLeaveOnInt
        };
    }
}

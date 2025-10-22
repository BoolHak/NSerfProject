namespace NSerf.Agent;

/// <summary>
/// Configuration for the Serf agent.
/// Ported from Go: serf/cmd/serf/command/agent/config.go
/// This is agent-level config (separate from core Serf.Config).
/// </summary>
public class AgentConfig
{
    /// <summary>
    /// Default bind port for Serf communication.
    /// </summary>
    public const int DefaultBindPort = 7946;
    
    /// <summary>
    /// Node name for this agent.
    /// </summary>
    public string NodeName { get; set; } = Environment.MachineName;
    
    /// <summary>
    /// Role for backwards compatibility (deprecated - use Tags instead).
    /// </summary>
    public string? Role { get; set; }
    
    /// <summary>
    /// Disable coordinate calculations.
    /// </summary>
    public bool DisableCoordinates { get; set; }
    
    /// <summary>
    /// Key/value metadata tags for this node.
    /// </summary>
    public Dictionary<string, string> Tags { get; set; } = new();
    
    /// <summary>
    /// Path to tags file for persistence.
    /// </summary>
    public string? TagsFile { get; set; }
    
    /// <summary>
    /// Bind address (IP:Port) for Serf communication.
    /// Default: "0.0.0.0:7946"
    /// </summary>
    public string BindAddr { get; set; } = $"0.0.0.0:{DefaultBindPort}";
    
    /// <summary>
    /// Advertise address for NAT traversal.
    /// </summary>
    public string? AdvertiseAddr { get; set; }
    
    /// <summary>
    /// Base64-encoded 32-byte encryption key.
    /// </summary>
    public string? EncryptKey { get; set; }
    
    /// <summary>
    /// Path to keyring file for encryption keys.
    /// </summary>
    public string? KeyringFile { get; set; }
    
    /// <summary>
    /// Log level: TRACE, DEBUG, INFO, WARN, ERROR.
    /// Default: INFO
    /// </summary>
    public string LogLevel { get; set; } = "INFO";
    
    /// <summary>
    /// RPC/IPC bind address.
    /// Default: "127.0.0.1:7373"
    /// </summary>
    public string RpcAddr { get; set; } = "127.0.0.1:7373";
    
    /// <summary>
    /// Authentication key for RPC/IPC.
    /// </summary>
    public string? RpcAuthKey { get; set; }
    
    /// <summary>
    /// Serf protocol version.
    /// Default: Max protocol version
    /// </summary>
    public int Protocol { get; set; } = 5; // serf.ProtocolVersionMax
    
    /// <summary>
    /// Replay past user events when joining.
    /// </summary>
    public bool ReplayOnJoin { get; set; }
    
    /// <summary>
    /// Query response size limit (bytes).
    /// </summary>
    public int QueryResponseSizeLimit { get; set; } = 1024;
    
    /// <summary>
    /// Query size limit (bytes).
    /// </summary>
    public int QuerySizeLimit { get; set; } = 1024;
    
    /// <summary>
    /// User event size limit (bytes).
    /// </summary>
    public int UserEventSizeLimit { get; set; } = 512;
    
    /// <summary>
    /// Nodes to join on startup.
    /// </summary>
    public string[] StartJoin { get; set; } = Array.Empty<string>();
    
    /// <summary>
    /// Event handler scripts to execute.
    /// </summary>
    public string[] EventHandlers { get; set; } = Array.Empty<string>();
    
    /// <summary>
    /// Timing profile: "lan", "wan", or "local".
    /// Default: "lan"
    /// </summary>
    public string Profile { get; set; } = "lan";
    
    /// <summary>
    /// Path to snapshot file.
    /// </summary>
    public string? SnapshotPath { get; set; }
    
    /// <summary>
    /// Leave cluster on SIGTERM.
    /// </summary>
    public bool LeaveOnTerm { get; set; }
    
    /// <summary>
    /// Skip leave on SIGINT.
    /// </summary>
    public bool SkipLeaveOnInt { get; set; }
    
    /// <summary>
    /// Discovery interface name.
    /// </summary>
    public string? Discover { get; set; }
    
    /// <summary>
    /// Interface name to bind to.
    /// </summary>
    public string? Interface { get; set; }
    
    /// <summary>
    /// Reconnect interval for failed nodes.
    /// </summary>
    public TimeSpan ReconnectInterval { get; set; }
    
    /// <summary>
    /// Reconnect timeout.
    /// </summary>
    public TimeSpan ReconnectTimeout { get; set; }
    
    /// <summary>
    /// Tombstone timeout.
    /// </summary>
    public TimeSpan TombstoneTimeout { get; set; }
    
    /// <summary>
    /// Disable name resolution.
    /// </summary>
    public bool DisableNameResolution { get; set; }
    
    /// <summary>
    /// Enable syslog logging.
    /// </summary>
    public bool EnableSyslog { get; set; }
    
    /// <summary>
    /// Syslog facility.
    /// </summary>
    public string SyslogFacility { get; set; } = "LOCAL0";
    
    /// <summary>
    /// Nodes to retry joining.
    /// </summary>
    public string[] RetryJoin { get; set; } = Array.Empty<string>();
    
    /// <summary>
    /// Max retry attempts for joining.
    /// </summary>
    public int RetryMaxAttempts { get; set; }
    
    /// <summary>
    /// Retry interval for joining.
    /// Default: 30 seconds
    /// </summary>
    public TimeSpan RetryInterval { get; set; } = TimeSpan.FromSeconds(30);
    
    /// <summary>
    /// Rejoin cluster after leave.
    /// </summary>
    public bool RejoinAfterLeave { get; set; }
    
    /// <summary>
    /// Statsite address for metrics.
    /// </summary>
    public string? StatsiteAddr { get; set; }
    
    /// <summary>
    /// StatsD address for metrics.
    /// </summary>
    public string? StatsdAddr { get; set; }
    
    /// <summary>
    /// Broadcast timeout.
    /// Default: 5 seconds
    /// </summary>
    public TimeSpan BroadcastTimeout { get; set; } = TimeSpan.FromSeconds(5);
    
    /// <summary>
    /// Enable gzip compression.
    /// Default: true
    /// </summary>
    public bool EnableCompression { get; set; } = true;
    
    /// <summary>
    /// Creates a default configuration.
    /// Ported from Go: DefaultConfig()
    /// </summary>
    public static AgentConfig Default()
    {
        return new AgentConfig
        {
            DisableCoordinates = false,
            Tags = new Dictionary<string, string>(),
            BindAddr = "0.0.0.0:7946",
            LogLevel = "INFO",
            RpcAddr = "127.0.0.1:7373",
            Protocol = 5, // Max protocol version
            ReplayOnJoin = false,
            Profile = "lan",
            RetryInterval = TimeSpan.FromSeconds(30),
            SyslogFacility = "LOCAL0",
            QueryResponseSizeLimit = 1024,
            QuerySizeLimit = 1024,
            UserEventSizeLimit = 512,
            BroadcastTimeout = TimeSpan.FromSeconds(5),
            EnableCompression = true
        };
    }
    
    /// <summary>
    /// Parses BindAddr into IP and port components.
    /// Ported from Go: AddrParts()
    /// </summary>
    public (string IP, int Port) ParseBindAddr()
    {
        if (string.IsNullOrEmpty(BindAddr))
        {
            return ("0.0.0.0", DefaultBindPort);
        }
        
        var parts = BindAddr.Split(':');
        if (parts.Length == 1)
        {
            // No port specified, use default
            return (parts[0], DefaultBindPort);
        }
        
        var ip = parts[0];
        var port = int.Parse(parts[1]);
        
        return (ip, port);
    }
    
    /// <summary>
    /// Decodes the base64-encoded encryption key.
    /// Ported from Go: EncryptBytes()
    /// </summary>
    public byte[] DecodeEncryptKey()
    {
        if (string.IsNullOrEmpty(EncryptKey))
        {
            return Array.Empty<byte>();
        }
        
        return Convert.FromBase64String(EncryptKey);
    }
}

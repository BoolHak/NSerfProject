namespace NSerf.Client;

/// <summary>
/// IPC protocol constants for Serf agent communication.
/// Defines command names, error messages, and protocol versions.
/// </summary>
public static class IpcProtocol
{
    /// <summary>
    /// Minimum supported IPC protocol version.
    /// </summary>
    public const int MinVersion = 1;
    
    /// <summary>
    /// Maximum supported IPC protocol version.
    /// </summary>
    public const int MaxVersion = 1;
    
    // ========== Commands (20 total) ==========
    
    /// <summary>
    /// Handshake command - establishes protocol version.
    /// </summary>
    public const string HandshakeCommand = "handshake";
    
    /// <summary>
    /// Auth command - authenticates with agent.
    /// </summary>
    public const string AuthCommand = "auth";
    
    /// <summary>
    /// Join command - joins cluster.
    /// </summary>
    public const string JoinCommand = "join";
    
    /// <summary>
    /// Leave command - gracefully leaves cluster.
    /// </summary>
    public const string LeaveCommand = "leave";
    
    /// <summary>
    /// Force-leave command - forces node to leave.
    /// </summary>
    public const string ForceLeaveCommand = "force-leave";
    
    /// <summary>
    /// Members command - lists all cluster members.
    /// </summary>
    public const string MembersCommand = "members";
    
    /// <summary>
    /// Members-filtered command - lists filtered members.
    /// </summary>
    public const string MembersFilteredCommand = "members-filtered";
    
    /// <summary>
    /// Event command - sends user event.
    /// </summary>
    public const string EventCommand = "event";
    
    /// <summary>
    /// Tags command - updates node tags.
    /// </summary>
    public const string TagsCommand = "tags";
    
    /// <summary>
    /// Stream command - subscribes to event stream.
    /// </summary>
    public const string StreamCommand = "stream";
    
    /// <summary>
    /// Monitor command - subscribes to log stream.
    /// </summary>
    public const string MonitorCommand = "monitor";
    
    /// <summary>
    /// Stop command - stops a stream.
    /// </summary>
    public const string StopCommand = "stop";
    
    /// <summary>
    /// Install-key command - installs encryption key.
    /// </summary>
    public const string InstallKeyCommand = "install-key";
    
    /// <summary>
    /// Use-key command - changes primary encryption key.
    /// </summary>
    public const string UseKeyCommand = "use-key";
    
    /// <summary>
    /// Remove-key command - removes encryption key.
    /// </summary>
    public const string RemoveKeyCommand = "remove-key";
    
    /// <summary>
    /// List-keys command - lists all encryption keys.
    /// </summary>
    public const string ListKeysCommand = "list-keys";
    
    /// <summary>
    /// Query command - initiates distributed query.
    /// </summary>
    public const string QueryCommand = "query";
    
    /// <summary>
    /// Respond command - responds to query.
    /// </summary>
    public const string RespondCommand = "respond";
    
    /// <summary>
    /// Stats command - retrieves agent statistics.
    /// </summary>
    public const string StatsCommand = "stats";
    
    /// <summary>
    /// Get-coordinate command - retrieves node coordinate.
    /// </summary>
    public const string GetCoordinateCommand = "get-coordinate";
    
    // ========== Error Messages (10 total) ==========
    
    /// <summary>
    /// Error when command is not recognized.
    /// </summary>
    public const string UnsupportedCommand = "Unsupported command";
    
    /// <summary>
    /// Error when IPC version is not supported.
    /// </summary>
    public const string UnsupportedIPCVersion = "Unsupported IPC version";
    
    /// <summary>
    /// Error when handshake is attempted twice.
    /// </summary>
    public const string DuplicateHandshake = "Handshake already performed";
    
    /// <summary>
    /// Error when command is sent before handshake.
    /// </summary>
    public const string HandshakeRequired = "Handshake required";
    
    /// <summary>
    /// Error when monitor already exists for client.
    /// </summary>
    public const string MonitorExists = "Monitor already exists";
    
    /// <summary>
    /// Error when event filter is invalid.
    /// </summary>
    public const string InvalidFilter = "Invalid event filter";
    
    /// <summary>
    /// Error when stream with sequence already exists.
    /// </summary>
    public const string StreamExists = "Stream with given sequence exists";
    
    /// <summary>
    /// Error when query ID does not match pending query.
    /// </summary>
    public const string InvalidQueryID = "No pending queries matching ID";
    
    /// <summary>
    /// Error when authentication is required but not performed.
    /// </summary>
    public const string AuthRequired = "Authentication required";
    
    /// <summary>
    /// Error when authentication token is invalid.
    /// </summary>
    public const string InvalidAuthToken = "Invalid authentication token";
    
    // ========== Query Record Types ==========
    
    /// <summary>
    /// Query record type for acknowledgment.
    /// </summary>
    public const string QueryRecordAck = "ack";
    
    /// <summary>
    /// Query record type for response.
    /// </summary>
    public const string QueryRecordResponse = "response";
    
    /// <summary>
    /// Query record type for completion signal.
    /// </summary>
    public const string QueryRecordDone = "done";
}

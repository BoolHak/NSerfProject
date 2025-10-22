using MessagePack;
using System.Net;

namespace NSerf.Client;

/// <summary>
/// Request header sent before each request.
/// </summary>
[MessagePackObject]
public class RequestHeader
{
    [Key(0)]
    public string Command { get; set; } = "";
    
    [Key(1)]
    public ulong Seq { get; set; }
}

/// <summary>
/// Response header sent before each response.
/// </summary>
[MessagePackObject]
public class ResponseHeader
{
    [Key(0)]
    public ulong Seq { get; set; }
    
    [Key(1)]
    public string Error { get; set; } = "";
}

/// <summary>
/// Represents a cluster member in IPC protocol.
/// </summary>
[MessagePackObject]
public class IpcMember
{
    [Key(0)] public string Name { get; set; } = "";
    [Key(1)] public byte[] Addr { get; set; } = Array.Empty<byte>();
    [Key(2)] public ushort Port { get; set; }
    [Key(3)] public Dictionary<string, string> Tags { get; set; } = new();
    [Key(4)] public string Status { get; set; } = "";
    [Key(5)] public byte ProtocolMin { get; set; }
    [Key(6)] public byte ProtocolMax { get; set; }
    [Key(7)] public byte ProtocolCur { get; set; }
    [Key(8)] public byte DelegateMin { get; set; }
    [Key(9)] public byte DelegateMax { get; set; }
    [Key(10)] public byte DelegateCur { get; set; }
    
    /// <summary>
    /// Converts byte array to IPAddress.
    /// </summary>
    public IPAddress GetIPAddress() => new IPAddress(Addr);
}

[MessagePackObject]
public class HandshakeRequest
{
    [Key(0)]
    public int Version { get; set; }
}

[MessagePackObject]
public class AuthRequest
{
    [Key(0)]
    public string AuthKey { get; set; } = "";
}

[MessagePackObject]
public class JoinRequest
{
    [Key(0)]
    public string[] Existing { get; set; } = Array.Empty<string>();
    
    [Key(1)]
    public bool Replay { get; set; }
}

[MessagePackObject]
public class JoinResponse
{
    [Key(0)]
    public int Num { get; set; }
}

[MessagePackObject]
public class MembersFilteredRequest
{
    [Key(0)]
    public Dictionary<string, string>? Tags { get; set; }
    
    [Key(1)]
    public string? Status { get; set; }
    
    [Key(2)]
    public string? Name { get; set; }
}

[MessagePackObject]
public class MembersResponse
{
    [Key(0)]
    public IpcMember[] Members { get; set; } = Array.Empty<IpcMember>();
}

[MessagePackObject]
public class EventRequest
{
    [Key(0)] public string Name { get; set; } = "";
    [Key(1)] public byte[] Payload { get; set; } = Array.Empty<byte>();
    [Key(2)] public bool Coalesce { get; set; }
}

[MessagePackObject]
public class ForceLeaveRequest
{
    [Key(0)] public string Node { get; set; } = "";
    [Key(1)] public bool Prune { get; set; }
}

[MessagePackObject]
public class TagsRequest
{
    [Key(0)] public Dictionary<string, string>? Tags { get; set; }
    [Key(1)] public string[]? DeleteTags { get; set; }
}

[MessagePackObject]
public class KeyRequest
{
    [Key(0)] public string Key { get; set; } = "";
}

[MessagePackObject]
public class KeyResponse
{
    [Key(0)] public Dictionary<string, string> Messages { get; set; } = new();
    [Key(1)] public Dictionary<string, int> Keys { get; set; } = new();
    [Key(2)] public int NumNodes { get; set; }
    [Key(3)] public int NumErr { get; set; }
    [Key(4)] public int NumResp { get; set; }
}

[MessagePackObject]
public class MonitorRequest
{
    [Key(0)] public string LogLevel { get; set; } = "";
}

[MessagePackObject]
public class StreamRequest
{
    [Key(0)] public string Type { get; set; } = "";
}

[MessagePackObject]
public class StopRequest
{
    [Key(0)] public ulong Stop { get; set; }
}

[MessagePackObject]
public class QueryRequest
{
    [Key(0)] public string[]? FilterNodes { get; set; }
    [Key(1)] public Dictionary<string, string>? FilterTags { get; set; }
    [Key(2)] public bool RequestAck { get; set; }
    [Key(3)] public byte RelayFactor { get; set; }
    [Key(4)] public long Timeout { get; set; } // TimeSpan.Ticks
    [Key(5)] public string Name { get; set; } = "";
    [Key(6)] public byte[] Payload { get; set; } = Array.Empty<byte>();
}

[MessagePackObject]
public class RespondRequest
{
    [Key(0)] public ulong ID { get; set; }
    [Key(1)] public byte[] Payload { get; set; } = Array.Empty<byte>();
}

[MessagePackObject]
public class QueryRecord
{
    [Key(0)] public string Type { get; set; } = ""; // "ack", "response", "done"
    [Key(1)] public string From { get; set; } = "";
    [Key(2)] public byte[] Payload { get; set; } = Array.Empty<byte>();
}

[MessagePackObject]
public class NodeResponse
{
    [Key(0)] public string From { get; set; } = "";
    [Key(1)] public byte[] Payload { get; set; } = Array.Empty<byte>();
}

[MessagePackObject]
public class LogRecord
{
    [Key(0)] public string Log { get; set; } = "";
}

[MessagePackObject]
public class UserEventRecord
{
    [Key(0)] public string Event { get; set; } = "user";
    [Key(1)] public ulong LTime { get; set; }
    [Key(2)] public string Name { get; set; } = "";
    [Key(3)] public byte[] Payload { get; set; } = Array.Empty<byte>();
    [Key(4)] public bool Coalesce { get; set; }
}

[MessagePackObject]
public class QueryEventRecord
{
    [Key(0)] public string Event { get; set; } = "query";
    [Key(1)] public ulong ID { get; set; }
    [Key(2)] public ulong LTime { get; set; }
    [Key(3)] public string Name { get; set; } = "";
    [Key(4)] public byte[] Payload { get; set; } = Array.Empty<byte>();
}

[MessagePackObject]
public class MemberEventRecord
{
    [Key(0)] public string Event { get; set; } = ""; // "member-join", etc.
    [Key(1)] public IpcMember[] Members { get; set; } = Array.Empty<IpcMember>();
}

[MessagePackObject]
public class CoordinateRequest
{
    [Key(0)] public string Node { get; set; } = "";
}

[MessagePackObject]
public class CoordinateResponse
{
    [Key(0)] public Coordinate.Coordinate Coord { get; set; } = new();
    [Key(1)] public bool Ok { get; set; }
}

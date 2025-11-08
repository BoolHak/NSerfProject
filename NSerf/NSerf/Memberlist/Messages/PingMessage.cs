using MessagePack;

namespace NSerf.Memberlist.Messages;

/// <summary>
/// Ping request sent directly to a node.
/// </summary>
[MessagePackObject]
public class PingMessage
{
    /// <summary>
    /// Sequence number for tracking the ping/ack pair.
    /// </summary>
    [Key(0)]
    public uint SeqNo { get; set; }

    /// <summary>
    /// Node name - sent so the target can verify they are the intended recipient.
    /// This protects against an agent restart with a new name.
    /// </summary>
    [Key(1)]
    public string Node { get; set; } = string.Empty;

    /// <summary>
    /// Source address, used for a direct reply (optional).
    /// </summary>
    [Key(2)]
    public byte[] SourceAddr { get; set; } = [];

    /// <summary>
    /// Source port, used for a direct reply (optional).
    /// </summary>
    [Key(3)]
    public ushort SourcePort { get; set; }

    /// <summary>
    /// Source node name, used for a direct reply (optional).
    /// </summary>
    [Key(4)]
    public string SourceNode { get; set; } = string.Empty;
}
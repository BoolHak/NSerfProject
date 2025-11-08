using MessagePack;

namespace NSerf.Memberlist.Messages;

/// <summary>
/// Indirect ping sent to an indirect node.
/// </summary>
[MessagePackObject]
public class IndirectPingMessage
{
    /// <summary>
    /// Sequence number for tracking.
    /// </summary>
    [Key(0)]
    public uint SeqNo { get; set; }

    /// <summary>
    /// Target node address.
    /// </summary>
    [Key(1)]
    public byte[] Target { get; set; } = [];

    /// <summary>
    /// Target node port.
    /// </summary>
    [Key(2)]
    public ushort Port { get; set; }

    /// <summary>
    /// Target node name - for verification.
    /// </summary>
    [Key(3)]
    public string Node { get; set; } = string.Empty;

    /// <summary>
    /// True if we'd like a nack back.
    /// </summary>
    [Key(4)]
    public bool Nack { get; set; }

    /// <summary>
    /// Source address, used for a direct reply (optional).
    /// </summary>
    [Key(5)]
    public byte[] SourceAddr { get; set; } = [];

    /// <summary>
    /// Source port, used for a direct reply (optional).
    /// </summary>
    [Key(6)]
    public ushort SourcePort { get; set; }

    /// <summary>
    /// Source node name, used for a direct reply (optional).
    /// </summary>
    [Key(7)]
    public string SourceNode { get; set; } = string.Empty;
}
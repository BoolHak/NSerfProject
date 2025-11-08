using MessagePack;

namespace NSerf.Memberlist.Messages;

/// <summary>
/// Alive message is broadcast when we know a node is alive.
/// Also used for nodes joining the cluster.
/// </summary>
[MessagePackObject]
public class AliveMessage
{
    /// <summary>
    /// Incarnation number of the alive node.
    /// </summary>
    [Key(0)]
    public uint Incarnation { get; set; }

    /// <summary>
    /// Name of the alive node.
    /// </summary>
    [Key(1)]
    public string Node { get; set; } = string.Empty;

    /// <summary>
    /// IP address of the node.
    /// </summary>
    [Key(2)]
    public byte[] Addr { get; set; } = [];

    /// <summary>
    /// Port number of the node.
    /// </summary>
    [Key(3)]
    public ushort Port { get; set; }

    /// <summary>
    /// Metadata from the delegate for this node.
    /// </summary>
    [Key(4)]
    public byte[] Meta { get; set; } = [];

    /// <summary>
    /// Protocol versions: [pmin, pmax, pcur, dmin, dmax, dcur].
    /// </summary>
    [Key(5)]
    public byte[] Vsn { get; set; } = [];
}
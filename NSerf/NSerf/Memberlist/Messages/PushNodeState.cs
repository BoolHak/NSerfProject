using MessagePack;
using NSerf.Memberlist.State;

namespace NSerf.Memberlist.Messages;

/// <summary>
/// Push node state used for push/pull requests when transferring node states.
/// </summary>
[MessagePackObject]
public class PushNodeState
{
    /// <summary>
    /// Node name.
    /// </summary>
    [Key(0)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// IP address.
    /// </summary>
    [Key(1)]
    public byte[] Addr { get; set; } = [];

    /// <summary>
    /// Port number.
    /// </summary>
    [Key(2)]
    public ushort Port { get; set; }

    /// <summary>
    /// Node metadata.
    /// </summary>
    [Key(3)]
    public byte[] Meta { get; set; } = [];

    /// <summary>
    /// Incarnation number.
    /// </summary>
    [Key(4)]
    public uint Incarnation { get; set; }

    /// <summary>
    /// Current state of the node.
    /// </summary>
    [Key(5)]
    public NodeStateType State { get; set; }

    /// <summary>
    /// Protocol versions.
    /// </summary>
    [Key(6)]
    public byte[] Vsn { get; set; } = [];
}
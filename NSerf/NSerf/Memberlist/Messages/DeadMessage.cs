using MessagePack;

namespace NSerf.Memberlist.Messages;

/// <summary>
/// Dead message is broadcast when we confirm a node is dead.
/// Also used for nodes leaving the cluster.
/// </summary>
[MessagePackObject]
public class DeadMessage
{
    /// <summary>
    /// Incarnation number of the dead node.
    /// </summary>
    [Key(0)]
    public uint Incarnation { get; set; }

    /// <summary>
    /// Name of the dead node.
    /// </summary>
    [Key(1)]
    public string Node { get; set; } = string.Empty;

    /// <summary>
    /// Name of the node reporting the death.
    /// </summary>
    [Key(2)]
    public string From { get; set; } = string.Empty;
}
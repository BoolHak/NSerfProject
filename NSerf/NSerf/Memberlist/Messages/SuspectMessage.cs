using MessagePack;

namespace NSerf.Memberlist.Messages;

/// <summary>
/// Suspect message is broadcast when we suspect a node is dead.
/// </summary>
[MessagePackObject]
public class SuspectMessage
{
    /// <summary>
    /// Incarnation number of the suspected node.
    /// </summary>
    [Key(0)]
    public uint Incarnation { get; set; }

    /// <summary>
    /// Name of the suspected node.
    /// </summary>
    [Key(1)]
    public string Node { get; set; } = string.Empty;

    /// <summary>
    /// Name of the node reporting the suspicion.
    /// </summary>
    [Key(2)]
    public string From { get; set; } = string.Empty;
}
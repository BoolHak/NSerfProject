using MessagePack;

namespace NSerf.Memberlist.Messages;

/// <summary>
/// Push/pull header used to inform the other side how many states we are transferring.
/// </summary>
[MessagePackObject]
public class PushPullHeader
{
    /// <summary>
    /// Number of node states being transferred.
    /// </summary>
    [Key(0)]
    public int Nodes { get; set; }

    /// <summary>
    /// Length of user state in bytes.
    /// </summary>
    [Key(1)]
    public int UserStateLen { get; set; }

    /// <summary>
    /// True if this is a join request, false for anti-entropy.
    /// </summary>
    [Key(2)]
    public bool Join { get; set; }
}
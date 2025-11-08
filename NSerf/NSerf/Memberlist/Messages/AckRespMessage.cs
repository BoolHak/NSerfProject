using MessagePack;

namespace NSerf.Memberlist.Messages;

/// <summary>
/// Acknowledgment response sent for a ping.
/// </summary>
[MessagePackObject]
public class AckRespMessage
{
    /// <summary>
    /// Sequence number matching the ping.
    /// </summary>
    [Key(0)]
    public uint SeqNo { get; set; }

    /// <summary>
    /// Optional payload data.
    /// </summary>
    [Key(1)]
    public byte[] Payload { get; set; } = [];
}
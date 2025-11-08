using MessagePack;

namespace NSerf.Memberlist.Messages;

/// <summary>
/// Negative acknowledgment response sent for an indirect ping when the pinger doesn't 
/// hear from the ping-ee within the configured timeout.
/// </summary>
[MessagePackObject]
public class NackRespMessage
{
    /// <summary>
    /// Sequence number matching the ping.
    /// </summary>
    [Key(0)]
    public uint SeqNo { get; set; }
}
using MessagePack;

namespace NSerf.Memberlist.Messages;

/// <summary>
/// User message header used to encapsulate a user message.
/// </summary>
[MessagePackObject]
public class UserMsgHeader
{
    /// <summary>
    /// Length of user message in bytes.
    /// </summary>
    [Key(0)]
    public int UserMsgLen { get; set; }
}
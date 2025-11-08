using MessagePack;

namespace NSerf.Memberlist.Messages;

/// <summary>
/// Error response sent to relay the error from the remote end.
/// </summary>
[MessagePackObject]
public class ErrRespMessage
{
    /// <summary>
    /// Error message text.
    /// </summary>
    [Key(0)]
    public string Error { get; set; } = string.Empty;
}
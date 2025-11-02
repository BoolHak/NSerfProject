// Ported from: github.com/hashicorp/memberlist/delegate.go
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

namespace NSerf.Memberlist.Delegates;

/// <summary>
/// Interface that clients must implement to hook into the gossip layer of Memberlist.
/// All methods must be thread-safe, as they can and generally will be called concurrently.
/// </summary>
public interface IDelegate
{
    /// <summary>
    /// Retrieves meta-data about the current node when broadcasting an alive message.
    /// Length is limited to the given byte size. This metadata is available in the Node structure.
    /// </summary>
    /// <param name="limit">Maximum size in bytes for the metadata.</param>
    /// <returns>Node metadata.</returns>
    byte[] NodeMeta(int limit);
    
    /// <summary>
    /// Called when a user-data message is received.
    /// Care should be taken that this method does not block, since doing so would block
    /// the entire UDP packet receive loop. The byte slice may be modified after the call
    /// returns, so it should be copied if needed.
    /// </summary>
    /// <param name="message">User data message received.</param>
    void NotifyMsg(ReadOnlySpan<byte> message);
    
    /// <summary>
    /// Called when user data messages can be broadcast. Can return a list of buffers to send.
    /// Each buffer should assume an overhead as provided with a limit on the total byte size allowed.
    /// The total byte size must not exceed the limit. Care should be taken that this method does not block.
    /// </summary>
    /// <param name="overhead">Overhead bytes per message.</param>
    /// <param name="limit">Total byte size limit.</param>
    /// <returns>List of messages to broadcast.</returns>
    List<byte[]> GetBroadcasts(int overhead, int limit);
    
    /// <summary>
    /// Used for a TCP Push/Pull. Sent to the remote side in addition to membership information.
    /// Any data can be sent here. See IMergeDelegate as well.
    /// </summary>
    /// <param name="join">True if this is for a join instead of a push/pull.</param>
    /// <returns>Local state data.</returns>
    byte[] LocalState(bool join);
    
    /// <summary>
    /// Invoked after a TCP Push/Pull. This is the state received from the remote side
    /// and is the result of the remote side's LocalState call.
    /// </summary>
    /// <param name="buffer">Remote state data.</param>
    /// <param name="join">True if this is for a join instead of a push/pull.</param>
    void MergeRemoteState(ReadOnlySpan<byte> buffer, bool join);
}

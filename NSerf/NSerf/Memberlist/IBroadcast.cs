// Ported from: github.com/hashicorp/memberlist/queue.go
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

namespace NSerf.Memberlist;

/// <summary>
/// Represents something that can be broadcasted via gossip to the memberlist cluster.
/// </summary>
public interface IBroadcast
{
    /// <summary>
    /// Checks if enqueuing the current broadcast invalidates a previous broadcast.
    /// </summary>
    /// <param name="other">Another broadcast to check against.</param>
    /// <returns>True if this broadcast invalidates the other, false otherwise.</returns>
    bool Invalidates(IBroadcast other);
    
    /// <summary>
    /// Returns the byte form of the message.
    /// </summary>
    byte[] Message();
    
    /// <summary>
    /// Invoked when the message will no longer be broadcast, either due to
    /// invalidation or to the transmitted limit being reached.
    /// </summary>
    void Finished();
}

/// <summary>
/// Optional extension of IBroadcast that gives each message a unique string name,
/// used for optimization. Implementations should ensure that Invalidates() checks
/// the same uniqueness as the Name() method.
/// </summary>
public interface INamedBroadcast : IBroadcast
{
    /// <summary>
    /// Returns the unique identity of this broadcast message.
    /// </summary>
    string Name();
}

/// <summary>
/// Optional interface that indicates each message is intrinsically unique, and
/// there is no need to scan the broadcast queue for duplicates.
/// Implementations should ensure that Invalidates() always returns false.
/// </summary>
public interface IUniqueBroadcast : IBroadcast
{
    // Marker interface - no additional methods
}

// Ported from: github.com/hashicorp/memberlist/state.go
// Copyright (c) HashiCorp, Inc.
// SPDX-License-Identifier: MPL-2.0

namespace NSerf.Memberlist;

/// <summary>
/// Thread-safe sequence and incarnation number generator.
/// </summary>
public class SequenceGenerator
{
    private uint _sequenceNum;
    private uint _incarnation;
    
    /// <summary>
    /// Returns the next sequence number in a thread-safe way.
    /// </summary>
    public uint NextSeqNo()
    {
        return Interlocked.Increment(ref _sequenceNum);
    }
    
    /// <summary>
    /// Returns the next incarnation number in a thread-safe way.
    /// </summary>
    public uint NextIncarnation()
    {
        return Interlocked.Increment(ref _incarnation);
    }
    
    /// <summary>
    /// Adds the positive offset to the incarnation number.
    /// </summary>
    public uint SkipIncarnation(uint offset)
    {
        return Interlocked.Add(ref _incarnation, offset);
    }
    
    /// <summary>
    /// Gets the current sequence number.
    /// </summary>
    public uint CurrentSeqNo => Volatile.Read(ref _sequenceNum);
    
    /// <summary>
    /// Gets the current incarnation number.
    /// </summary>
    public uint CurrentIncarnation => Volatile.Read(ref _incarnation);
}

// Ported from: github.com/hashicorp/memberlist
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

namespace NSerf.Memberlist;

/// <summary>
/// Result of joining a cluster.
/// </summary>
public class JoinResult
{
    public int NumJoined { get; set; }
    public List<string> SuccessfulNodes { get; set; } = new();
    public List<string> FailedNodes { get; set; } = new();
    public List<Exception> Errors { get; set; } = new();
    
    public bool Success => NumJoined > 0;
}

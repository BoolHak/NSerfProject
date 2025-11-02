// Ported from: github.com/hashicorp/memberlist
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

namespace NSerf.Memberlist;

/// <summary>
/// Result of leaving a cluster.
/// </summary>
public class LeaveResult
{
    public bool Success { get; set; }
    public TimeSpan BroadcastTimeout { get; set; }
    public Exception? Error { get; set; }
}

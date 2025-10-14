// Ported from: github.com/hashicorp/memberlist
// Copyright (c) HashiCorp, Inc.
// SPDX-License-Identifier: MPL-2.0

namespace NSerf.Memberlist;

/// <summary>
/// Result of probing a node.
/// </summary>
public class ProbeResult
{
    public bool Success { get; set; }
    public string NodeName { get; set; } = string.Empty;
    public TimeSpan Rtt { get; set; }
    public bool UsedTcp { get; set; }
    public int IndirectChecks { get; set; }
}

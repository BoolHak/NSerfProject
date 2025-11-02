// Ported from: github.com/hashicorp/memberlist
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.Net;

namespace NSerf.Memberlist;

/// <summary>
/// Request to ping a node.
/// </summary>
public class PingRequest
{
    public uint SeqNo { get; set; }
    public string NodeName { get; set; } = string.Empty;
    public IPEndPoint? Target { get; set; }
    public TimeSpan Timeout { get; set; }
}

/// <summary>
/// Response from a ping.
/// </summary>
public class PingResponse
{
    public bool Success { get; set; }
    public TimeSpan Rtt { get; set; }
    public byte[]? Payload { get; set; }
    public string? Error { get; set; }
}

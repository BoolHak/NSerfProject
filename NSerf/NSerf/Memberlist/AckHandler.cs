// Ported from: github.com/hashicorp/memberlist/state.go
// Copyright (c) HashiCorp, Inc.
// SPDX-License-Identifier: MPL-2.0

namespace NSerf.Memberlist;

/// <summary>
/// Used to register handlers for incoming acks and nacks.
/// </summary>
internal class AckHandler : IDisposable
{
    public Action<byte[], DateTimeOffset>? AckFn { get; set; }
    public Action? NackFn { get; set; }
    public Timer? Timer { get; set; }
    
    public void Dispose()
    {
        Timer?.Dispose();
    }
}

/// <summary>
/// Exception used to indicate a 'ping' packet was successfully issued
/// but no response was received.
/// </summary>
public class NoPingResponseException : Exception
{
    public string NodeName { get; }
    
    public NoPingResponseException(string nodeName) 
        : base($"No response from node {nodeName}")
    {
        NodeName = nodeName;
    }
}

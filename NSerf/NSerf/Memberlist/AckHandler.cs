// Ported from: github.com/hashicorp/memberlist/state.go
// Copyright (c) Boolhak, Inc.
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
    private bool _disposed;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            // Dispose managed resources
            Timer?.Dispose();
            Timer = null;
            AckFn = null;
            NackFn = null;
        }

        _disposed = true;
    }
}

/// <summary>
/// Exception used to indicate a 'ping' packet was successfully issued
/// but no response was received.
/// </summary>
public class NoPingResponseException(string nodeName) : Exception($"No response from node {nodeName}")
{
    public string NodeName { get; } = nodeName;
}

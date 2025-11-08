// Ported from: github.com/hashicorp/memberlist/state.go
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

namespace NSerf.Memberlist.Handlers;

/// <summary>
/// Used to register handlers for incoming acks and nacks.
/// </summary>
internal sealed class AckHandler : IDisposable
{
    public Action<byte[], DateTimeOffset>? AckFn { get; set; }
    public Action? NackFn { get; set; }
    public Timer? Timer { get; set; }
    private bool _disposed;

    public void Dispose()
    {
        Dispose(true);
    }

    private void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            // Dispose of managed resources
            Timer?.Dispose();
            Timer = null;
            AckFn = null;
            NackFn = null;
        }

        _disposed = true;
    }
}

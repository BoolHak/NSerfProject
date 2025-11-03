// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.Net.Sockets;
using MessagePack;
using static NSerf.Agent.CircularLogWriter;

namespace NSerf.Agent.RPC;

/// <summary>
/// RPC log handler for monitor command streaming.
/// </summary>
public sealed class RpcLogHandler(NetworkStream stream, SemaphoreSlim writeLock, CancellationToken cancellationToken) : ILogHandler, IDisposable
{
    private bool _disposed;

    private static readonly MessagePackSerializerOptions MsgPackOptions =
        MessagePackSerializerOptions.Standard
            .WithCompression(MessagePackCompression.None);

    public void HandleLog(string log)
    {
        if (_disposed || cancellationToken.IsCancellationRequested)
            return;

        try
        {
            writeLock.Wait(cancellationToken);
            try
            {
                var logBytes = MessagePackSerializer.Serialize(log, MsgPackOptions, cancellationToken);
                stream.Write(logBytes);
                stream.Flush();
            }
            finally
            {
                writeLock.Release();
            }
        }
        catch
        {
            // Stream closed or error
        }
    }

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
            // Nothing to dispose of; resources are external.
        }

        _disposed = true;
    }
}

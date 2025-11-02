// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.Net.Sockets;
using MessagePack;
using NSerf.Client;
using static NSerf.Agent.CircularLogWriter;

namespace NSerf.Agent.RPC;

/// <summary>
/// RPC log handler for monitor command streaming.
/// </summary>
public class RpcLogHandler(NetworkStream stream, SemaphoreSlim writeLock, CancellationToken cancellationToken) : ILogHandler, IDisposable
{
    private readonly NetworkStream _stream = stream;
    private readonly SemaphoreSlim _writeLock = writeLock;
    private readonly CancellationToken _cancellationToken = cancellationToken;
    private bool _disposed;

    private static readonly MessagePackSerializerOptions MsgPackOptions =
        MessagePackSerializerOptions.Standard
            .WithCompression(MessagePackCompression.None);

    public void HandleLog(string log)
    {
        if (_disposed || _cancellationToken.IsCancellationRequested)
            return;

        try
        {
            _writeLock.Wait(_cancellationToken);
            try
            {
                var logBytes = MessagePackSerializer.Serialize(log, MsgPackOptions, _cancellationToken);
                _stream.Write(logBytes);
                _stream.Flush();
            }
            finally
            {
                _writeLock.Release();
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
            // Nothing to dispose; resources are external.
        }

        _disposed = true;
    }
}

// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.Net.Sockets;
using MessagePack;
using NSerf.Client;
using NSerf.Serf;
using NSerf.Serf.Events;

namespace NSerf.Agent.RPC;

/// <summary>
/// RPC event handler for stream command.
/// </summary>
public class RpcEventHandler : IEventHandler, IDisposable
{
    private readonly NetworkStream _stream;
    private readonly SemaphoreSlim _writeLock;
    private readonly CancellationToken _cancellationToken;
    private readonly string? _eventFilter;
    private bool _disposed;

    private static readonly MessagePackSerializerOptions MsgPackOptions =
        MessagePackSerializerOptions.Standard
            .WithCompression(MessagePackCompression.None);

    public RpcEventHandler(NetworkStream stream, SemaphoreSlim writeLock, string? eventFilter, CancellationToken cancellationToken)
    {
        _stream = stream;
        _writeLock = writeLock;
        _eventFilter = eventFilter;
        _cancellationToken = cancellationToken;
    }

    public void HandleEvent(Event @event)
    {
        if (_disposed || _cancellationToken.IsCancellationRequested)
            return;

        // Filter by event type if specified
        var eventType = @event.EventType();
        if (!string.IsNullOrEmpty(_eventFilter) && eventType.String() != _eventFilter)
            return;

        try
        {
            _writeLock.Wait(_cancellationToken);
            try
            {
                var streamEvent = ConvertToStreamEvent(@event);
                var eventBytes = MessagePackSerializer.Serialize(streamEvent, MsgPackOptions, _cancellationToken);
                _stream.Write(eventBytes);
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

    private Client.Responses.StreamEvent ConvertToStreamEvent(Event @event)
    {
        var eventType = @event.EventType();
        var streamEvent = new Client.Responses.StreamEvent
        {
            Event = eventType.String().ToLowerInvariant()
        };

        // Handle different event types
        switch (@event)
        {
            case MemberEvent memberEvent:
                streamEvent.Members = [.. memberEvent.Members.Select(m => new Client.Responses.Member
                {
                    Name = m.Name,
                    Addr = System.Text.Encoding.UTF8.GetBytes(m.Addr.ToString()),
                    Port = m.Port,
                    Tags = m.Tags,
                    Status = m.Status.ToString().ToLowerInvariant(),
                    ProtocolMin = m.ProtocolMin,
                    ProtocolMax = m.ProtocolMax,
                    ProtocolCur = m.ProtocolCur,
                    DelegateMin = m.DelegateMin,
                    DelegateMax = m.DelegateMax,
                    DelegateCur = m.DelegateCur
                })];
                break;

            case UserEvent userEvent:
                streamEvent.LTime = userEvent.LTime;
                streamEvent.Name = userEvent.Name;
                streamEvent.Payload = userEvent.Payload;
                break;

            case Query query:
                streamEvent.LTime = query.LTime;
                streamEvent.Name = query.Name;
                streamEvent.Payload = query.Payload;
                break;
        }

        return streamEvent;
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

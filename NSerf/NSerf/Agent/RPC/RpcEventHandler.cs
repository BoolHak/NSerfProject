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

    public void HandleEvent(Event evt)
    {
        if (_disposed || _cancellationToken.IsCancellationRequested)
            return;

        // Filter by event type if specified
        var eventType = evt.EventType();
        if (!string.IsNullOrEmpty(_eventFilter) && eventType.String() != _eventFilter)
            return;

        try
        {
            _writeLock.Wait(_cancellationToken);
            try
            {
                var streamEvent = ConvertToStreamEvent(evt);
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

    private Client.Responses.StreamEvent ConvertToStreamEvent(Event evt)
    {
        var eventType = evt.EventType();
        var streamEvent = new Client.Responses.StreamEvent
        {
            Event = eventType.String().ToLowerInvariant()
        };

        // Handle different event types
        switch (evt)
        {
            case MemberEvent memberEvent:
                streamEvent.Members = memberEvent.Members.Select(m => new Client.Responses.Member
                {
                    Name = m.Name,
                    Addr = System.Text.Encoding.UTF8.GetBytes(m.Addr.ToString()),
                    Port = (ushort)m.Port,
                    Tags = m.Tags,
                    Status = m.Status.ToString().ToLowerInvariant(),
                    ProtocolMin = m.ProtocolMin,
                    ProtocolMax = m.ProtocolMax,
                    ProtocolCur = m.ProtocolCur,
                    DelegateMin = m.DelegateMin,
                    DelegateMax = m.DelegateMax,
                    DelegateCur = m.DelegateCur
                }).ToArray();
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
        _disposed = true;
    }
}

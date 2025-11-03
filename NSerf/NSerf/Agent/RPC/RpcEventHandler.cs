// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.Net.Sockets;
using MessagePack;
using NSerf.Serf.Events;

namespace NSerf.Agent.RPC;

/// <summary>
/// RPC event handler for stream command.
/// </summary>
public sealed class RpcEventHandler(NetworkStream stream, SemaphoreSlim writeLock, string? eventFilter, CancellationToken cancellationToken) : IEventHandler, IDisposable
{
    private bool _disposed;

    private static readonly MessagePackSerializerOptions MsgPackOptions =
        MessagePackSerializerOptions.Standard
            .WithCompression(MessagePackCompression.None);

    public void HandleEvent(IEvent @event)
    {
        if (_disposed || cancellationToken.IsCancellationRequested)
            return;

        // Filter by event type if specified
        var eventType = @event.EventType();
        if (!string.IsNullOrEmpty(eventFilter) && eventType.String() != eventFilter)
            return;

        try
        {
            writeLock.Wait(cancellationToken);
            try
            {
                var streamEvent = ConvertToStreamEvent(@event);
                var eventBytes = MessagePackSerializer.Serialize(streamEvent, MsgPackOptions, cancellationToken);
                stream.Write(eventBytes);
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

    private static Client.Responses.StreamEvent ConvertToStreamEvent(IEvent @event)
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

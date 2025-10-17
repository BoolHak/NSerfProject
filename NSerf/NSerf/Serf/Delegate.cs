// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Ported from: github.com/hashicorp/serf/serf/delegate.go

using MessagePack;
using Microsoft.Extensions.Logging;
using NSerf.Memberlist;
using NSerf.Memberlist.Delegates;

namespace NSerf.Serf;

/// <summary>
/// Delegate is the memberlist.Delegate implementation that Serf uses.
/// Acts as a bridge between Serf and Memberlist, handling gossip messages and state synchronization.
/// </summary>
internal class Delegate : IDelegate
{
    private readonly Serf _serf;

    public Delegate(Serf serf)
    {
        _serf = serf ?? throw new ArgumentNullException(nameof(serf));
    }

    /// <summary>
    /// Retrieves meta-data about the current node when broadcasting an alive message.
    /// Encodes member tags into a format suitable for gossip transmission.
    /// </summary>
    public byte[] NodeMeta(int limit)
    {
        var roleBytes = _serf.EncodeTags(_serf.Config.Tags);
        
        if (roleBytes.Length > limit)
        {
            throw new InvalidOperationException(
                $"Node tags '{string.Join(", ", _serf.Config.Tags.Select(kvp => $"{kvp.Key}={kvp.Value}"))}' exceeds length limit of {limit} bytes");
        }

        return roleBytes;
    }

    /// <summary>
    /// Called when a user-data message is received from the network.
    /// Routes messages to appropriate handlers based on message type.
    /// </summary>
    public void NotifyMsg(ReadOnlySpan<byte> message)
    {
        // If we didn't actually receive any data, then ignore it.
        if (message.Length == 0)
        {
            return;
        }

        // Record metrics
        _serf.RecordMessageReceived(message.Length);

        bool rebroadcast = false;
        BroadcastQueue? rebroadcastQueue = _serf.Broadcasts;
        var messageType = (MessageType)message[0];

        // Check if message should be dropped (for testing)
        if (_serf.Config.ShouldDropMessage(messageType))
        {
            return;
        }

        // Process message based on type
        switch (messageType)
        {
            case MessageType.Leave:
                var leave = DecodeMessage<MessageLeave>(message[1..]);
                if (leave != null)
                {
                    _serf.Logger?.LogDebug("[Serf] messageLeaveType: {Node}", leave.Node);
                    rebroadcast = _serf.HandleNodeLeaveIntent(leave);
                }
                break;

            case MessageType.Join:
                var join = DecodeMessage<MessageJoin>(message[1..]);
                if (join != null)
                {
                    _serf.Logger?.LogDebug("[Serf] messageJoinType: {Node}", join.Node);
                    rebroadcast = _serf.HandleNodeJoinIntent(join);
                }
                break;

            case MessageType.UserEvent:
                var userEvent = DecodeMessage<MessageUserEvent>(message[1..]);
                if (userEvent != null)
                {
                    _serf.Logger?.LogDebug("[Serf] messageUserEventType: {Name}", userEvent.Name);
                    rebroadcast = _serf.HandleUserEvent(userEvent);
                    rebroadcastQueue = _serf.EventBroadcasts;
                }
                break;

            case MessageType.Query:
                var query = DecodeMessage<MessageQuery>(message[1..]);
                if (query != null)
                {
                    _serf.Logger?.LogDebug("[Serf] messageQueryType: {Name}", query.Name);
                    rebroadcast = _serf.HandleQuery(query);
                    rebroadcastQueue = _serf.QueryBroadcasts;
                }
                break;

            case MessageType.QueryResponse:
                var resp = DecodeMessage<MessageQueryResponse>(message[1..]);
                if (resp != null)
                {
                    _serf.Logger?.LogDebug("[Serf] messageQueryResponseType: {From}", resp.From);
                    _serf.HandleQueryResponse(resp);
                }
                break;

            case MessageType.Relay:
                HandleRelayMessage(message[1..]);
                break;

            default:
                _serf.Logger?.LogWarning("[Serf] Received message of unknown type: {Type}", messageType);
                break;
        }

        // Rebroadcast if needed
        if (rebroadcast && rebroadcastQueue != null)
        {
            // Copy the buffer since we cannot rely on the slice not changing
            var newBuf = message.ToArray();

            rebroadcastQueue.QueueBytes(newBuf);
        }
    }

    /// <summary>
    /// Handles relay messages which forward messages to specific destination nodes.
    /// </summary>
    private void HandleRelayMessage(ReadOnlySpan<byte> payload)
    {
        try
        {
            // Decode the relay header
            var header = MessagePackSerializer.Deserialize<RelayHeader>(payload.ToArray());

            // The remaining contents are the message itself
            var headerSize = MessagePackSerializer.Serialize(header).Length;
            var raw = payload[headerSize..].ToArray();

            _serf.Logger?.LogDebug("[Serf] Relaying response to addr: {Addr}", header.DestAddr);

            // Forward the message - will be fully implemented with Memberlist integration
            // For now, just log
            _serf.Logger?.LogWarning("[Serf] Relay forwarding not yet fully implemented");
        }
        catch (Exception ex)
        {
            _serf.Logger?.LogError(ex, "[Serf] Error handling relay message");
        }
    }

    /// <summary>
    /// Called when user data messages can be broadcast.
    /// Collects broadcasts from different queues (regular, query, event).
    /// </summary>
    public List<byte[]> GetBroadcasts(int overhead, int limit)
    {
        _serf.Logger?.LogInformation("[Serf.Delegate] *** GetBroadcasts called with overhead={Overhead}, limit={Limit} ***", overhead, limit);
        
        // Get regular broadcasts
        var msgs = _serf.Broadcasts.GetBroadcasts(overhead, limit);

        // Determine the bytes used already
        int bytesUsed = 0;
        foreach (var msg in msgs)
        {
            int msgLen = msg.Length;
            bytesUsed += msgLen + overhead;
            _serf.RecordMessageSent(msgLen);
        }

        // Get query broadcasts
        var queryMsgs = _serf.QueryBroadcasts.GetBroadcasts(overhead, limit - bytesUsed);
        if (queryMsgs.Count > 0)
        {
            foreach (var m in queryMsgs)
            {
                bytesUsed += m.Length + overhead;
                _serf.RecordMessageSent(m.Length);
            }
            msgs.AddRange(queryMsgs);
        }

        // Get event broadcasts
        var eventMsgs = _serf.EventBroadcasts.GetBroadcasts(overhead, limit - bytesUsed);
        if (eventMsgs.Count > 0)
        {
            _serf.Logger?.LogInformation("[Serf.Delegate] *** Retrieved {Count} event broadcasts ***", eventMsgs.Count);
            foreach (var m in eventMsgs)
            {
                bytesUsed += m.Length + overhead;
                _serf.RecordMessageSent(m.Length);
            }
            msgs.AddRange(eventMsgs);
        }

        _serf.Logger?.LogTrace("[Serf.Delegate] GetBroadcasts returning {Count} total messages", msgs.Count);
        return msgs;
    }

    /// <summary>
    /// Used for a TCP Push/Pull. Creates a snapshot of local state to send to remote node.
    /// </summary>
    public byte[] LocalState(bool join)
    {
        _serf.AcquireMemberLock();
        _serf.AcquireEventLock();

        try
        {
            // Create the push/pull message
            var pushPull = new MessagePushPull
            {
                LTime = _serf.Clock.Time(),
                StatusLTimes = new Dictionary<string, LamportTime>(_serf.MemberStates.Count),
                LeftMembers = new List<string>(_serf.LeftMembers.Count),
                EventLTime = _serf.EventClock.Time(),
                Events = _serf.EventBuffer.Values.ToList(),
                QueryLTime = _serf.QueryClock.Time()
            };

            // Add all the join LTimes
            foreach (var (name, member) in _serf.MemberStates)
            {
                pushPull.StatusLTimes[name] = member.StatusLTime;
            }

            // Add all the left nodes
            foreach (var member in _serf.LeftMembers)
            {
                pushPull.LeftMembers.Add(member.Name);
            }

            // Encode the push pull state
            return _serf.EncodeMessage(MessageType.PushPull, pushPull);
        }
        catch (Exception ex)
        {
            _serf.Logger?.LogError(ex, "[Serf] Failed to encode local state");
            return Array.Empty<byte>();
        }
        finally
        {
            _serf.ReleaseEventLock();
            _serf.ReleaseMemberLock();
        }
    }

    /// <summary>
    /// Invoked after a TCP Push/Pull. Merges remote state received from another node.
    /// </summary>
    public void MergeRemoteState(ReadOnlySpan<byte> buffer, bool join)
    {
        // Ensure we have a message
        if (buffer.Length == 0)
        {
            _serf.Logger?.LogError("[Serf] Remote state is zero bytes");
            return;
        }

        // Check the message type
        if ((MessageType)buffer[0] != MessageType.PushPull)
        {
            _serf.Logger?.LogError("[Serf] Remote state has bad type prefix: {Type}", buffer[0]);
            return;
        }

        // Check if we should drop this message
        if (_serf.Config.ShouldDropMessage(MessageType.PushPull))
        {
            return;
        }

        // Decode the message
        var pushPull = DecodeMessage<MessagePushPull>(buffer[1..]);
        if (pushPull == null)
        {
            return;
        }

        // Witness the Lamport clocks first
        // We subtract 1 since no message with that clock has been sent yet
        if (pushPull.LTime > 0)
        {
            _serf.Clock.Witness(pushPull.LTime - 1);
        }
        if (pushPull.EventLTime > 0)
        {
            _serf.EventClock.Witness(pushPull.EventLTime - 1);
        }
        if (pushPull.QueryLTime > 0)
        {
            _serf.QueryClock.Witness(pushPull.QueryLTime - 1);
        }

        // Process the left nodes first
        var leftMap = new HashSet<string>(pushPull.LeftMembers);
        var leave = new MessageLeave();
        
        foreach (var name in pushPull.LeftMembers)
        {
            if (pushPull.StatusLTimes.TryGetValue(name, out var statusLTime))
            {
                leave.LTime = statusLTime + 1;
                leave.Node = name;
                _serf.HandleNodeLeaveIntent(leave);
            }
        }

        // Update any other LTimes
        var joinMsg = new MessageJoin();
        foreach (var (name, statusLTime) in pushPull.StatusLTimes)
        {
            // Skip the left nodes
            if (leftMap.Contains(name))
            {
                continue;
            }

            // Create an artificial join message
            joinMsg.LTime = statusLTime;
            joinMsg.Node = name;
            _serf.HandleNodeJoinIntent(joinMsg);
        }

        // Handle event join ignore
        if (join && _serf.EventJoinIgnore)
        {
            _serf.AcquireEventLock();
            try
            {
                if (pushPull.EventLTime > _serf.EventMinTime)
                {
                    _serf.EventMinTime = pushPull.EventLTime;
                }
            }
            finally
            {
                _serf.ReleaseEventLock();
            }
        }

        // Process all the events
        var userEvent = new MessageUserEvent();
        foreach (var events in pushPull.Events)
        {
            if (events == null) continue;

            userEvent.LTime = events.LTime;
            foreach (var e in events.Events)
            {
                userEvent.Name = e.Name;
                userEvent.Payload = e.Payload;
                _serf.HandleUserEvent(userEvent);
            }
        }
    }

    /// <summary>
    /// Helper to decode MessagePack messages with error handling.
    /// </summary>
    private T? DecodeMessage<T>(ReadOnlySpan<byte> data) where T : class
    {
        try
        {
            return MessagePackSerializer.Deserialize<T>(data.ToArray());
        }
        catch (Exception ex)
        {
            _serf.Logger?.LogError(ex, "[Serf] Error decoding {Type} message", typeof(T).Name);
            return null;
        }
    }
}

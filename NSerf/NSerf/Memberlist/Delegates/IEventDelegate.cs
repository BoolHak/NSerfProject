// Ported from: github.com/hashicorp/memberlist/event_delegate.go
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Memberlist.State;
using System.Threading.Channels;

namespace NSerf.Memberlist.Delegates;

/// <summary>
/// Simpler delegate used only to receive notifications about members joining and leaving.
/// The methods may be called by multiple threads, but never concurrently.
/// This allows you to reason about ordering.
/// </summary>
public interface IEventDelegate
{
    /// <summary>
    /// Invoked when a node is detected to have joined.
    /// The Node argument must not be modified.
    /// </summary>
    void NotifyJoin(Node node);

    /// <summary>
    /// Invoked when a node is detected to have left.
    /// The Node argument must not be modified.
    /// </summary>
    void NotifyLeave(Node node);

    /// <summary>
    /// Invoked when a node is detected to have updated, usually involving metadata.
    /// The Node argument must not be modified.
    /// </summary>
    void NotifyUpdate(Node node);
}

/// <summary>
/// Types of node events that can occur.
/// </summary>
public enum NodeEventType
{
    /// <summary>
    /// Node joined the cluster.
    /// </summary>
    NodeJoin = 0,

    /// <summary>
    /// Node left the cluster.
    /// </summary>
    NodeLeave = 1,

    /// <summary>
    /// Node updated (usually metadata).
    /// </summary>
    NodeUpdate = 2
}

/// <summary>
/// Single event related to node activity in the memberlist.
/// </summary>
public class NodeEvent
{
    /// <summary>
    /// Type of event.
    /// </summary>
    public NodeEventType EventType { get; set; }

    /// <summary>
    /// Node involved in the event. Should not be directly modified.
    /// </summary>
    public Node Node { get; set; } = new();
}

/// <summary>
/// Event delegate that sends events over a channel instead of direct function calls.
/// Care must be taken that events are processed in a timely manner from the channel,
/// since this delegate will block until an event can be sent.
/// </summary>
public class ChannelEventDelegate : IEventDelegate
{
    private readonly ChannelWriter<NodeEvent> _channel;

    public ChannelEventDelegate(ChannelWriter<NodeEvent> channel)
    {
        _channel = channel;
    }

    public void NotifyJoin(Node node)
    {
        // Create a copy to avoid modification issues
        var nodeCopy = CloneNode(node);
        _channel.TryWrite(new NodeEvent
        {
            EventType = NodeEventType.NodeJoin,
            Node = nodeCopy
        });
    }

    public void NotifyLeave(Node node)
    {
        var nodeCopy = CloneNode(node);
        _channel.TryWrite(new NodeEvent
        {
            EventType = NodeEventType.NodeLeave,
            Node = nodeCopy
        });
    }

    public void NotifyUpdate(Node node)
    {
        var nodeCopy = CloneNode(node);
        _channel.TryWrite(new NodeEvent
        {
            EventType = NodeEventType.NodeUpdate,
            Node = nodeCopy
        });
    }

    private static Node CloneNode(Node node)
    {
        return new Node
        {
            Name = node.Name,
            Addr = node.Addr,
            Port = node.Port,
            Meta = [.. node.Meta],
            State = node.State,
            PMin = node.PMin,
            PMax = node.PMax,
            PCur = node.PCur,
            DMin = node.DMin,
            DMax = node.DMax,
            DCur = node.DCur
        };
    }
}

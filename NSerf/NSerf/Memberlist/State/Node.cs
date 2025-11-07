// Ported from: github.com/hashicorp/memberlist/state.go
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.Net;
using NSerf.Memberlist.Common;
using NSerf.Memberlist.Transport;

namespace NSerf.Memberlist.State;

/// <summary>
/// Represents a node in the cluster.
/// </summary>
public class Node
{
    /// <summary>
    /// Unique name of the node in the cluster.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// IP address of the node.
    /// </summary>
    public IPAddress Addr { get; set; } = IPAddress.None;

    /// <summary>
    /// Port number the node is listening on.
    /// </summary>
    public ushort Port { get; set; }

    /// <summary>
    /// Metadata from the delegate for this node.
    /// </summary>
    public byte[] Meta { get; set; } = [];

    /// <summary>
    /// Current state of the node.
    /// </summary>
    public NodeStateType State { get; set; }

    /// <summary>
    /// Minimum protocol version this node understands.
    /// </summary>
    public byte PMin { get; set; }

    /// <summary>
    /// Maximum protocol version this node understands.
    /// </summary>
    public byte PMax { get; set; }

    /// <summary>
    /// Current protocol version the node is speaking.
    /// </summary>
    public byte PCur { get; set; }

    /// <summary>
    /// Minimum delegate protocol version this node understands.
    /// </summary>
    public byte DMin { get; set; }

    /// <summary>
    /// Maximum delegate protocol version this node understands.
    /// </summary>
    public byte DMax { get; set; }

    /// <summary>
    /// Current delegate protocol version the node is speaking.
    /// </summary>
    public byte DCur { get; set; }

    /// <summary>
    /// Returns the host:port form of the node's address.
    /// </summary>
    public string Address()
    {
        return NetworkUtils.JoinHostPort(Addr.ToString(), Port);
    }

    /// <summary>
    /// Returns the node name and host:port form of the node's address.
    /// </summary>
    public Address FullAddress()
    {
        return new Address
        {
            Addr = Address(),
            Name = Name
        };
    }

    /// <summary>
    /// Returns the node name.
    /// </summary>
    public override string ToString()
    {
        return Name;
    }
}


/// <summary>
/// Internal state tracking for a node.
/// </summary>
public class NodeState
{
    /// <summary>
    /// The node information.
    /// </summary>
    public Node Node { get; set; } = new();

    /// <summary>
    /// Last known incarnation number.
    /// </summary>
    public uint Incarnation { get; set; }

    /// <summary>
    /// Current state of the node.
    /// </summary>
    public NodeStateType State { get; set; }

    /// <summary>
    /// Time when the last state change occurred.
    /// </summary>
    public DateTimeOffset StateChange { get; set; }

    /// <summary>
    /// Returns the host:port form of the node's address.
    /// </summary>
    public string Address() => Node.Address();

    /// <summary>
    /// Returns the full address of the node.
    /// </summary>
    public Address FullAddress() => Node.FullAddress();

    /// <summary>
    /// Returns true if the node is dead or has left the cluster.
    /// </summary>
    public bool DeadOrLeft()
    {
        return State is NodeStateType.Dead or NodeStateType.Left;
    }

    /// <summary>
    /// Gets the node name.
    /// </summary>
    public string Name => Node.Name;

    /// <summary>
    /// Converts the NodeState to a Node instance.
    /// </summary>
    public Node ToNode()
    {
        return new Node
        {
            Name = Node.Name,
            Addr = Node.Addr,
            Port = Node.Port,
            Meta = Node.Meta,
            State = State,
            PMin = Node.PMin,
            PMax = Node.PMax,
            PCur = Node.PCur,
            DMin = Node.DMin,
            DMax = Node.DMax,
            DCur = Node.DCur
        };
    }
}

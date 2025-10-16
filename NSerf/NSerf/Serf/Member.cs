// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Ported from: github.com/hashicorp/serf/serf/serf.go

using System.Net;

namespace NSerf.Serf;

/// <summary>
/// Member is a single member of the Serf cluster.
/// Contains all information about a node in the cluster including its
/// address, tags, protocol versions, and current status.
/// </summary>
public class Member
{
    /// <summary>
    /// Unique name of the member in the cluster.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// IP address of the member.
    /// </summary>
    public IPAddress Addr { get; set; } = IPAddress.None;

    /// <summary>
    /// Port the member is listening on for cluster communication.
    /// </summary>
    public ushort Port { get; set; }

    /// <summary>
    /// Tags associated with this member (metadata key-value pairs).
    /// </summary>
    public Dictionary<string, string> Tags { get; set; } = new();

    /// <summary>
    /// Current status of the member (Alive, Leaving, Left, Failed, None).
    /// </summary>
    public MemberStatus Status { get; set; }

    // Protocol version fields - the minimum, maximum, and current values
    // of the protocol versions and delegate (Serf) protocol versions that
    // each member can understand or is speaking.

    /// <summary>
    /// Minimum memberlist protocol version this member can understand.
    /// </summary>
    public byte ProtocolMin { get; set; }

    /// <summary>
    /// Maximum memberlist protocol version this member can understand.
    /// </summary>
    public byte ProtocolMax { get; set; }

    /// <summary>
    /// Current memberlist protocol version this member is speaking.
    /// </summary>
    public byte ProtocolCur { get; set; }

    /// <summary>
    /// Minimum Serf delegate protocol version this member can understand.
    /// </summary>
    public byte DelegateMin { get; set; }

    /// <summary>
    /// Maximum Serf delegate protocol version this member can understand.
    /// </summary>
    public byte DelegateMax { get; set; }

    /// <summary>
    /// Current Serf delegate protocol version this member is speaking.
    /// </summary>
    public byte DelegateCur { get; set; }

    /// <summary>
    /// Returns a string representation of the member.
    /// </summary>
    public override string ToString()
    {
        return $"{Name} ({Addr}:{Port}) - {Status.ToStatusString()}";
    }

    /// <summary>
    /// Creates a copy of the member.
    /// </summary>
    public Member Clone()
    {
        return new Member
        {
            Name = Name,
            Addr = Addr,
            Port = Port,
            Tags = new Dictionary<string, string>(Tags),
            Status = Status,
            ProtocolMin = ProtocolMin,
            ProtocolMax = ProtocolMax,
            ProtocolCur = ProtocolCur,
            DelegateMin = DelegateMin,
            DelegateMax = DelegateMax,
            DelegateCur = DelegateCur
        };
    }
}

// Ported from: github.com/hashicorp/memberlist/config.go
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.Net;
using Microsoft.Extensions.Logging;
using NSerf.Memberlist.Delegates;
using NSerf.Memberlist.Security;
using NSerf.Memberlist.Transport;

namespace NSerf.Memberlist.Configuration;

/// <summary>
/// Configuration for a Memberlist instance.
/// </summary>
public class MemberlistConfig
{
    /// <summary>
    /// The name of this node. This must be unique in the cluster.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Transport for communicating with other nodes.
    /// If null, NetTransport will be created using BindAddr and BindPort.
    /// </summary>
    public ITransport? Transport { get; set; }

    /// <summary>
    /// Optional label to include on the outside of each packet and stream.
    /// If gossip encryption is enabled, this is treated as GCM authenticated data.
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Skips the check that inbound packets and gossip streams need to be label prefixed.
    /// </summary>
    public bool SkipInboundLabelCheck { get; set; }

    /// <summary>
    /// Address to bind to for UDP and TCP gossip.
    /// </summary>
    public string BindAddr { get; set; } = "0.0.0.0";

    /// <summary>
    /// Port to listen on (both UDP and TCP).
    /// </summary>
    public int BindPort { get; set; } = 7946;

    /// <summary>
    /// Address to advertise to other cluster members (for NAT traversal).
    /// </summary>
    public string AdvertiseAddr { get; set; } = string.Empty;

    /// <summary>
    /// Port to advertise to other cluster members.
    /// </summary>
    public int AdvertisePort { get; set; } = 7946;

    /// <summary>
    /// Protocol version we will speak. Must be between ProtocolVersionMin and ProtocolVersionMax.
    /// </summary>
    public byte ProtocolVersion { get; set; } = NSerf.Memberlist.ProtocolVersion.Version2Compatible;

    /// <summary>
    /// Timeout for establishing a stream connection and for stream read/write operations.
    /// </summary>
    public TimeSpan TCPTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Number of nodes that will be asked to perform an indirect probe when a direct probe fails.
    /// </summary>
    public int IndirectChecks { get; set; } = 3;

    /// <summary>
    /// Multiplier for the number of retransmissions attempted for broadcasted messages.
    /// Actual retransmits = RetransmitMult * log(N+1)
    /// </summary>
    public int RetransmitMult { get; set; } = 4;

    /// <summary>
    /// Multiplier for determining suspicion timeout before declaring a node dead.
    /// SuspicionTimeout = SuspicionMult * log(N+1) * ProbeInterval
    /// </summary>
    public int SuspicionMult { get; set; } = 4;

    /// <summary>
    /// Multiplier applied to SuspicionTimeout as an upper bound on detection time.
    /// </summary>
    public int SuspicionMaxTimeoutMult { get; set; } = 6;

    /// <summary>
    /// Interval between complete state syncs over TCP. Zero disables push/pull syncs.
    /// </summary>
    public TimeSpan PushPullInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Interval between random node probes.
    /// </summary>
    public TimeSpan ProbeInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Timeout to wait for an ack from a probed node.
    /// Should be set to 99-percentile of RTT on your network.
    /// </summary>
    public TimeSpan ProbeTimeout { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Turns off fallback TCP pings if direct UDP ping fails.
    /// </summary>
    public bool DisableTcpPings { get; set; }

    /// <summary>
    /// Function to control TCP pings on a per-node basis.
    /// </summary>
    public Func<string, bool>? DisableTcpPingsForNode { get; set; }

    /// <summary>
    /// Will increase the probe interval if the node becomes aware it might be degraded.
    /// </summary>
    public int AwarenessMaxMultiplier { get; set; } = 8;

    /// <summary>
    /// Interval between sending messages that need to be gossiped. Zero disables non-piggyback gossip.
    /// </summary>
    public TimeSpan GossipInterval { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Number of random nodes to send gossip messages to per GossipInterval.
    /// </summary>
    public int GossipNodes { get; set; } = 3;

    /// <summary>
    /// Interval after which a dead node will still receive gossip (gives it a chance to refute).
    /// </summary>
    public TimeSpan GossipToTheDeadTime { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Controls whether to enforce encryption for incoming gossip.
    /// </summary>
    public bool GossipVerifyIncoming { get; set; } = true;

    /// <summary>
    /// Controls whether to enforce encryption for outgoing gossip.
    /// </summary>
    public bool GossipVerifyOutgoing { get; set; } = true;

    /// <summary>
    /// Controls message compression to reduce bandwidth at the cost of CPU.
    /// </summary>
    public bool EnableCompression { get; set; } = true;

    /// <summary>
    /// Used to initialize the primary encryption key in the keyring.
    /// Should be 16, 24, or 32 bytes for AES-128, AES-192, or AES-256.
    /// </summary>
    public byte[]? SecretKey { get; set; }

    /// <summary>
    /// Holds all encryption keys. Automatically initialized using SecretKey.
    /// </summary>
    public Keyring? Keyring { get; set; }

    /// <summary>
    /// Path to system's DNS config file (usually /etc/resolv.conf).
    /// </summary>
    public string DNSConfigPath { get; set; } = "/etc/resolv.conf";

    /// <summary>
    /// Logger for memberlist operations.
    /// </summary>
    public ILogger? Logger { get; set; }

    /// <summary>
    /// Size of internal channel for UDP message handling.
    /// </summary>
    public int HandoffQueueDepth { get; set; } = 1024;

    /// <summary>
    /// Maximum bytes per UDP packet. Default 1400 is safe for most networks.
    /// </summary>
    public int UDPBufferSize { get; set; } = 1400;

    public bool StealthUdp { get; set; }

    /// <summary>
    /// Time before a dead node's name can be reclaimed by another node. Zero means no reclaim.
    /// </summary>
    public TimeSpan DeadNodeReclaimTime { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// Controls if node name is required when sending messages.
    /// </summary>
    public bool RequireNodeNames { get; set; }

    /// <summary>
    /// Networks allowed to connect. Null allows any, empty list blocks all.
    /// </summary>
    public List<IPNetwork> CIDRsAllowed { get; set; } = [];

    /// <summary>
    /// Interval at which message queue depth is checked.
    /// </summary>
    public TimeSpan QueueCheckInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Force msgpack codec to use new time.Time format.
    /// </summary>
    public bool MsgpackUseNewTimeFormat { get; set; }

    /// <summary>
    /// Delegate for receiving and providing data to memberlist via callbacks.
    /// </summary>
    public IDelegate? Delegate { get; set; }

    /// <summary>
    /// Current delegate protocol version being spoken.
    /// </summary>
    public byte DelegateProtocolVersion { get; set; }

    /// <summary>
    /// Minimum delegate protocol version understood.
    /// </summary>
    public byte DelegateProtocolMin { get; set; }

    /// <summary>
    /// Maximum delegate protocol version understood.
    /// </summary>
    public byte DelegateProtocolMax { get; set; }

    /// <summary>
    /// Event delegate for notifications about members joining/leaving.
    /// </summary>
    public IEventDelegate? Events { get; set; }

    /// <summary>
    /// Conflict delegate for handling name conflicts.
    /// </summary>
    public IConflictDelegate? Conflict { get; set; }

    /// <summary>
    /// Merge delegate for controlling cluster merge operations.
    /// </summary>
    public IMergeDelegate? Merge { get; set; }

    /// <summary>
    /// Ping delegate for RTT measurements and ack payloads.
    /// </summary>
    public IPingDelegate? Ping { get; set; }

    /// <summary>
    /// Alive delegate for filtering nodes during join.
    /// </summary>
    public IAliveDelegate? Alive { get; set; }

    /// <summary>
    /// Returns a sane configuration optimized for LAN environments.
    /// </summary>
    public static MemberlistConfig DefaultLANConfig()
    {
        var hostname = Environment.MachineName;
        return new MemberlistConfig
        {
            Name = hostname,
            BindAddr = "0.0.0.0",
            BindPort = 7946,
            AdvertiseAddr = string.Empty,
            AdvertisePort = 7946,
            ProtocolVersion = NSerf.Memberlist.ProtocolVersion.Version2Compatible,
            TCPTimeout = TimeSpan.FromSeconds(10),
            IndirectChecks = 3,
            RetransmitMult = 4,
            SuspicionMult = 4,
            SuspicionMaxTimeoutMult = 6,
            PushPullInterval = TimeSpan.FromSeconds(30),
            ProbeTimeout = TimeSpan.FromMilliseconds(500),
            ProbeInterval = TimeSpan.FromSeconds(1),
            DisableTcpPings = false,
            AwarenessMaxMultiplier = 8,
            GossipNodes = 3,
            GossipInterval = TimeSpan.FromMilliseconds(200),
            GossipToTheDeadTime = TimeSpan.FromSeconds(30),
            GossipVerifyIncoming = true,
            GossipVerifyOutgoing = true,
            EnableCompression = true,
            SecretKey = null,
            Keyring = null,
            DNSConfigPath = "/etc/resolv.conf",
            HandoffQueueDepth = 1024,
            UDPBufferSize = 1400,
            CIDRsAllowed = [],
            QueueCheckInterval = TimeSpan.FromSeconds(30)
        };
    }

    /// <summary>
    /// Returns a configuration optimized for WAN environments.
    /// </summary>
    public static MemberlistConfig DefaultWANConfig()
    {
        var config = DefaultLANConfig();
        config.TCPTimeout = TimeSpan.FromSeconds(30);
        config.SuspicionMult = 6;
        config.PushPullInterval = TimeSpan.FromSeconds(60);
        config.ProbeTimeout = TimeSpan.FromSeconds(3);
        config.ProbeInterval = TimeSpan.FromSeconds(5);
        config.GossipNodes = 4;
        config.GossipInterval = TimeSpan.FromMilliseconds(500);
        config.GossipToTheDeadTime = TimeSpan.FromSeconds(60);
        config.StealthUdp = true;
        return config;
    }

    /// <summary>
    /// Returns a configuration optimized for local loopback environments.
    /// </summary>
    public static MemberlistConfig DefaultLocalConfig()
    {
        var config = DefaultLANConfig();
        config.TCPTimeout = TimeSpan.FromSeconds(1);
        config.IndirectChecks = 1;
        config.RetransmitMult = 2;
        config.SuspicionMult = 3;
        config.PushPullInterval = TimeSpan.FromSeconds(15);
        config.ProbeTimeout = TimeSpan.FromMilliseconds(200);
        config.ProbeInterval = TimeSpan.FromSeconds(1);
        config.GossipInterval = TimeSpan.FromMilliseconds(100);
        config.GossipToTheDeadTime = TimeSpan.FromSeconds(15);
        return config;
    }

    /// <summary>
    /// Returns true if IP access control must be checked.
    /// </summary>
    public bool IPMustBeChecked()
    {
        return CIDRsAllowed.Count > 0;
    }

    /// <summary>
    /// Returns an error message if the IP is not allowed, null if allowed.
    /// </summary>
    public string? IPAllowed(IPAddress ip)
    {
        if (!IPMustBeChecked())
        {
            return null;
        }

        return CIDRsAllowed.Any(network => network.Contains(ip))
            ? null
            : $"{ip} is not allowed";
    }

    /// <summary>
    /// Returns true if encryption is enabled.
    /// </summary>
    public bool EncryptionEnabled()
    {
        return Keyring != null && Keyring.GetKeys().Count > 0;
    }
}

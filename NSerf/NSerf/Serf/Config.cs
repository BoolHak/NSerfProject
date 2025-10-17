// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Ported from: github.com/hashicorp/serf/serf/config.go

using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSerf.Memberlist.Configuration;
using NSerf.Serf.Events;

namespace NSerf.Serf;

/// <summary>
/// ProtocolVersionMap is the mapping of Serf delegate protocol versions
/// to memberlist protocol versions. We mask the memberlist protocols using
/// our own protocol version.
/// </summary>
public static class ProtocolVersionMap
{
    public static readonly IReadOnlyDictionary<byte, byte> Mapping = new Dictionary<byte, byte>
    {
        { 5, 2 },
        { 4, 2 },
        { 3, 2 },
        { 2, 2 }
    };

    public const byte ProtocolVersionMin = 2;
    public const byte ProtocolVersionMax = 5;
}

/// <summary>
/// Config is the configuration for creating a Serf instance.
/// </summary>
public class Config
{
    /// <summary>
    /// The name of this node. This must be unique in the cluster. If this
    /// is not set, Serf will set it to the hostname of the running machine.
    /// </summary>
    public string NodeName { get; set; } = string.Empty;

    /// <summary>
    /// The tags for this role, if any. This is used to provide arbitrary
    /// key/value metadata per-node. For example, a "role" tag may be used to
    /// differentiate "load-balancer" from a "web" role as parts of the same cluster.
    /// </summary>
    public Dictionary<string, string> Tags { get; set; } = new();

    /// <summary>
    /// EventCh is a channel that receives all the Serf events. The events
    /// are sent on this channel in proper ordering. Care must be taken that
    /// this channel doesn't block, either by processing the events quick
    /// enough or buffering the channel, otherwise it can block state updates
    /// within Serf itself.
    /// </summary>
    public ChannelWriter<Event>? EventCh { get; set; }

    /// <summary>
    /// ProtocolVersion is the protocol version to speak. This must be between
    /// ProtocolVersionMin and ProtocolVersionMax.
    /// </summary>
    public byte ProtocolVersion { get; set; } = 4;

    /// <summary>
    /// BroadcastTimeout is the amount of time to wait for a broadcast
    /// message to be sent to the cluster. Broadcast messages are used for
    /// things like leave messages and force remove messages.
    /// </summary>
    public TimeSpan BroadcastTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// LeavePropagateDelay is for our leave (node dead) message to propagate
    /// through the cluster. In particular, we want to stay up long enough to
    /// service any probes from other nodes before they learn about us
    /// leaving and stop probing.
    /// </summary>
    public TimeSpan LeavePropagateDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// CoalescePeriod specifies the time duration to coalesce events.
    /// For example, if this is set to 5 seconds, then all events received
    /// within 5 seconds that can be coalesced will be.
    /// Coalescence is disabled by default (TimeSpan.Zero).
    /// </summary>
    public TimeSpan CoalescePeriod { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// QuiescentPeriod specifies the duration of time where if no events
    /// are received, coalescence immediately happens.
    /// </summary>
    public TimeSpan QuiescentPeriod { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// UserCoalescePeriod operates like CoalescePeriod but only affects
    /// user messages and not the Member* messages that Serf generates.
    /// </summary>
    public TimeSpan UserCoalescePeriod { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// UserQuiescentPeriod operates like QuiescentPeriod but only affects
    /// user messages.
    /// </summary>
    public TimeSpan UserQuiescentPeriod { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// ReapInterval is the interval when the reaper runs.
    /// </summary>
    public TimeSpan ReapInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// ReconnectInterval is the interval when we attempt to reconnect
    /// to failed nodes.
    /// </summary>
    public TimeSpan ReconnectInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// ReconnectTimeout is the amount of time to attempt to reconnect to
    /// a failed node before giving up and considering it completely gone.
    /// </summary>
    public TimeSpan ReconnectTimeout { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// TombstoneTimeout is the amount of time to keep around nodes
    /// that gracefully left as tombstones for syncing state with other
    /// Serf nodes.
    /// </summary>
    public TimeSpan TombstoneTimeout { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// FlapTimeout is the amount of time less than which we consider a node
    /// being failed and rejoining looks like a flap for telemetry purposes.
    /// </summary>
    public TimeSpan FlapTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// QueueCheckInterval is the interval at which we check the message
    /// queue to apply the warning and max depth.
    /// </summary>
    public TimeSpan QueueCheckInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// QueueDepthWarning is used to generate warning message if the
    /// number of queued messages to broadcast exceeds this number.
    /// </summary>
    public int QueueDepthWarning { get; set; } = 128;

    /// <summary>
    /// MaxQueueDepth is used to start dropping messages if the number
    /// of queued messages to broadcast exceeds this number.
    /// </summary>
    public int MaxQueueDepth { get; set; } = 4096;

    /// <summary>
    /// MinQueueDepth, if >0 will enforce a lower limit for dropping messages
    /// and then the max will be max(MinQueueDepth, 2*SizeOfCluster).
    /// Defaults to 0 which disables this dynamic sizing feature.
    /// </summary>
    public int MinQueueDepth { get; set; } = 0;

    /// <summary>
    /// RecentIntentTimeout is used to determine how long we store recent
    /// join and leave intents. This is used to guard against the case where
    /// Serf broadcasts an intent that arrives before the Memberlist event.
    /// </summary>
    public TimeSpan RecentIntentTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// EventBuffer is used to control how many events are buffered.
    /// This is used to prevent re-delivery of events to a client.
    /// </summary>
    public int EventBuffer { get; set; } = 512;

    /// <summary>
    /// QueryBuffer is used to control how many queries are buffered.
    /// This is used to prevent re-delivery of queries to a client.
    /// </summary>
    public int QueryBuffer { get; set; } = 512;

    /// <summary>
    /// QueryTimeoutMult configures the default timeout multiplier for a query to run if no
    /// specific value is provided. Timeout = GossipInterval * QueryTimeoutMult * log(N+1)
    /// </summary>
    public int QueryTimeoutMult { get; set; } = 16;

    /// <summary>
    /// QueryResponseSizeLimit limits the inbound payload sizes for queries.
    /// These must fit in a UDP packet with some additional overhead.
    /// </summary>
    public int QueryResponseSizeLimit { get; set; } = 1024;

    /// <summary>
    /// QuerySizeLimit limits the outbound payload sizes for queries.
    /// </summary>
    public int QuerySizeLimit { get; set; } = 1024;

    /// <summary>
    /// UserEventSizeLimit is the maximum byte size limit of a user event.
    /// Set to 9216 bytes by default (9KB), which is byte aligned
    /// </summary>
    public int UserEventSizeLimit { get; set; } = 9 * 1024;

    /// <summary>
    /// The memberlist configuration that Serf will use to do the
    /// underlying membership management and gossip. Some fields are
    /// overridden from the given ProtocolVersion.
    /// </summary>
    public MemberlistConfig? MemberlistConfig { get; set; }

    /// <summary>
    /// Logger is a custom logger which you provide.
    /// If not set, NullLogger will be used.
    /// </summary>
    public ILogger Logger { get; set; } = NullLogger.Instance;

    /// <summary>
    /// SnapshotPath if provided is used to snapshot live nodes as well
    /// as lamport clock values. When Serf is started with a snapshot,
    /// it will attempt to join all the previously known nodes until one
    /// succeeds and will also avoid replaying old user events.
    /// </summary>
    public string? SnapshotPath { get; set; }

    /// <summary>
    /// MinSnapshotSize is the minimum size threshold for snapshot compaction.
    /// Defaults to 128KB.
    /// </summary>
    public int MinSnapshotSize { get; set; } = 128 * 1024;

    /// <summary>
    /// RejoinAfterLeave controls if Serf will ignore a previous leave and
    /// rejoin the cluster. By default this is false, preventing nodes from
    /// rejoining after leaving.
    /// </summary>
    public bool RejoinAfterLeave { get; set; } = false;

    /// <summary>
    /// EnableNameConflictResolution controls if Serf will actively attempt
    /// to resolve a name conflict.
    /// </summary>
    public bool EnableNameConflictResolution { get; set; } = true;

    /// <summary>
    /// DisableCoordinates controls if Serf will maintain an estimate of this
    /// node's network coordinate internally.
    /// </summary>
    public bool DisableCoordinates { get; set; } = false;

    /// <summary>
    /// KeyringFile provides the location of a writable file where Serf can
    /// persist changes to the encryption keyring.
    /// </summary>
    public string? KeyringFile { get; set; }

    /// <summary>
    /// Merge can be optionally provided to intercept a cluster merge
    /// and conditionally abort the merge.
    /// </summary>
    public IMergeDelegate? Merge { get; set; }

    /// <summary>
    /// MessageDropper is a callback used for selectively ignoring inbound
    /// gossip messages. This should only be used in unit tests.
    /// WARNING: this should ONLY be used in tests
    /// </summary>
    internal Func<MessageType, bool>? MessageDropper { get; set; }

    /// <summary>
    /// Helper method to check if a message should be dropped.
    /// </summary>
    internal bool ShouldDropMessage(MessageType messageType)
    {
        return MessageDropper?.Invoke(messageType) ?? false;
    }

    /// <summary>
    /// ReconnectTimeoutOverride is an optional interface which when present allows
    /// the application to cause reaping of a node to happen when it otherwise wouldn't.
    /// </summary>
    public IReconnectTimeoutOverrider? ReconnectTimeoutOverride { get; set; }

    /// <summary>
    /// ValidateNodeNames controls whether nodenames only
    /// contain alphanumeric, dashes and '.' characters
    /// and sets maximum length to 128 characters.
    /// </summary>
    public bool ValidateNodeNames { get; set; } = false;

    /// <summary>
    /// MsgpackUseNewTimeFormat is used to force the underlying msgpack codec to
    /// use the newer format of time.Time when encoding.
    /// </summary>
    public bool MsgpackUseNewTimeFormat { get; set; } = false;

    /// <summary>
    /// Init allocates the sub-data structures.
    /// </summary>
    public void Init()
    {
        Tags ??= new Dictionary<string, string>();
        MessageDropper ??= _ => false;
    }

    /// <summary>
    /// DefaultConfig returns a Config struct that contains reasonable defaults
    /// for most of the configurations.
    /// </summary>
    public static Config DefaultConfig()
    {
        var hostname = Environment.MachineName;

        return new Config
        {
            NodeName = hostname,
            BroadcastTimeout = TimeSpan.FromSeconds(5),
            LeavePropagateDelay = TimeSpan.FromSeconds(1),
            EventBuffer = 512,
            QueryBuffer = 512,
            Logger = NullLogger.Instance,
            ProtocolVersion = 4,
            ReapInterval = TimeSpan.FromSeconds(15),
            RecentIntentTimeout = TimeSpan.FromMinutes(5),
            ReconnectInterval = TimeSpan.FromSeconds(30),
            ReconnectTimeout = TimeSpan.FromHours(24),
            QueueCheckInterval = TimeSpan.FromSeconds(30),
            QueueDepthWarning = 128,
            MaxQueueDepth = 4096,
            TombstoneTimeout = TimeSpan.FromHours(24),
            FlapTimeout = TimeSpan.FromSeconds(60),
            MemberlistConfig = MemberlistConfig.DefaultLANConfig(),
            QueryTimeoutMult = 16,
            QueryResponseSizeLimit = 1024,
            QuerySizeLimit = 1024,
            EnableNameConflictResolution = true,
            DisableCoordinates = false,
            ValidateNodeNames = false,
            UserEventSizeLimit = 512
        };
    }
}

/// <summary>
/// Interface for merge delegate that can conditionally abort cluster merges.
/// Will be fully implemented in Phase 6+ when core Serf functionality is ported.
/// </summary>
public interface IMergeDelegate
{
    /// <summary>
    /// NotifyMerge is invoked when members are discovered during a join operation.
    /// </summary>
    /// <param name="members">The members being merged</param>
    /// <returns>Error if merge should be aborted, null otherwise</returns>
    Task<string?> NotifyMerge(Member[] members);
}

/// <summary>
/// Interface for overriding reconnect timeout logic on a per-member basis.
/// Will be fully implemented in Phase 6+ when core Serf functionality is ported.
/// </summary>
public interface IReconnectTimeoutOverrider
{
    /// <summary>
    /// ReconnectTimeout is called to get the reconnect timeout for a specific member.
    /// </summary>
    /// <param name="member">The member to get timeout for</param>
    /// <param name="timeout">The default timeout</param>
    /// <returns>The timeout to use for this member</returns>
    TimeSpan ReconnectTimeout(Member member, TimeSpan timeout);
}

// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Ported from: github.com/hashicorp/serf/serf/serf.go

namespace NSerf.Serf;

/// <summary>
/// NodeIntent is used to buffer intents for out-of-order deliveries.
/// When we receive an intent message before the corresponding memberlist event,
/// we buffer it here. This is indexed by node name and always stores the
/// latest lamport time and intent type we've seen.
/// </summary>
internal class NodeIntent
{
    /// <summary>
    /// Type of intent being tracked.
    /// Only Join and Leave intents are tracked.
    /// </summary>
    public MessageType Type { get; set; }

    /// <summary>
    /// Wall clock time when we saw this intent.
    /// Used to expire old intents from the buffer.
    /// </summary>
    public DateTime WallTime { get; set; }

    /// <summary>
    /// Lamport time of the intent.
    /// Used for cluster-wide ordering of events.
    /// </summary>
    public LamportTime LTime { get; set; }

    /// <summary>
    /// Creates a string representation of the node intent.
    /// </summary>
    public override string ToString()
    {
        return $"Intent: {Type}, LTime: {LTime}, WallTime: {WallTime}";
    }
}

/// <summary>
/// MessageType identifies the type of Serf message.
/// Used internally for intent tracking and message routing.
/// </summary>
internal enum MessageType : byte
{
    /// <summary>
    /// Join message - member is joining the cluster.
    /// </summary>
    Join = 0,

    /// <summary>
    /// Leave message - member is leaving the cluster gracefully.
    /// </summary>
    Leave = 1,

    /// <summary>
    /// User event message - custom application event.
    /// </summary>
    UserEvent = 2,

    /// <summary>
    /// Query message - distributed query request.
    /// </summary>
    Query = 3,

    /// <summary>
    /// Query response message - response to a query.
    /// </summary>
    QueryResponse = 4,

    /// <summary>
    /// Conflict message - address/name conflict resolution.
    /// </summary>
    Conflict = 5,

    /// <summary>
    /// Key request message - encryption key management.
    /// </summary>
    KeyRequest = 6,

    /// <summary>
    /// Key response message - encryption key response.
    /// </summary>
    KeyResponse = 7,

    /// <summary>
    /// Relay message - message forwarding.
    /// </summary>
    Relay = 8,

    /// <summary>
    /// Error response message - error in query processing.
    /// </summary>
    ErrorResponse = 9,

    /// <summary>
    /// Push/pull message - state synchronization.
    /// </summary>
    PushPull = 10
}

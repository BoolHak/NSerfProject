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


// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

namespace NSerf.Serf.Events;

/// <summary>
/// Types of events that can occur in Serf.
/// Minimal implementation for Phase 0 - will be expanded in Phase 3.
/// </summary>
public enum EventType
{
    /// <summary>
    /// Member joined the cluster.
    /// </summary>
    MemberJoin = 0,

    /// <summary>
    /// Member left the cluster.
    /// </summary>
    MemberLeave = 1,

    /// <summary>
    /// Member failed.
    /// </summary>
    MemberFailed = 2,

    /// <summary>
    /// Member was updated.
    /// </summary>
    MemberUpdate = 3,

    /// <summary>
    /// Member was reaped.
    /// </summary>
    MemberReap = 4,

    /// <summary>
    /// User-defined event.
    /// </summary>
    User = 5,

    /// <summary>
    /// Query event.
    /// </summary>
    Query = 6
}

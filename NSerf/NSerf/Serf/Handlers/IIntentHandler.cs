// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

namespace NSerf.Serf.Handlers;

/// <summary>
/// Handles join and leave intent messages.
/// STATELESS - all states are managed through MemberManager.
/// Phase 3: Extracted from Serf.cs for better composition and testability.
/// </summary>
internal interface IIntentHandler
{
    /// <summary>
    /// Handles a join intent message.
    /// CRITICAL: Implements auto-rejoin prevention for Left/Failed members.
    /// </summary>
    /// <param name="joinIntent">The join intent message</param>
    /// <returns>True if the intent should be rebroadcast</returns>
    bool HandleJoinIntent(MessageJoin joinIntent);
    
    /// <summary>
    /// Handles a leave intent message.
    /// </summary>
    /// <param name="leaveIntent">The leave intent message</param>
    /// <returns>True if the intent should be rebroadcast</returns>
    bool HandleLeaveIntent(MessageLeave leaveIntent);
}

// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Logging;
using NSerf.Serf.Events;
using NSerf.Serf.Managers;
using NSerf.Serf.StateMachine;

namespace NSerf.Serf.Handlers;

/// <summary>
/// Handles join and leave intent messages using the StateMachine pattern.
/// STATELESS - delegates all state management to MemberManager.
/// </summary>
internal class IntentHandler(
    IMemberManager memberManager,
    List<IEvent> eventLog,
    LamportClock clock,
    ILogger? logger,
    string? localNodeName = null,
    Func<SerfState>? getSerfState = null,
    Action<byte[]>? broadcastJoinIntent = null) : IIntentHandler
{
    private readonly IMemberManager _memberManager = memberManager ?? throw new ArgumentNullException(nameof(memberManager));
    private readonly List<IEvent> _eventLog = eventLog ?? throw new ArgumentNullException(nameof(eventLog));
    private readonly LamportClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly string _localNodeName = localNodeName ?? string.Empty;
    private readonly Func<SerfState> _getSerfState = getSerfState ?? (() => SerfState.SerfAlive);
    private readonly Action<byte[]>? _broadcastJoinIntent = broadcastJoinIntent;

    /// <summary>
    /// Handles a join intent message.
    /// CRITICAL: Left/Failed members cannot be resurrected via join intent.
    /// Only Leaving → Alive transition is allowed (refutation).
    /// </summary>
    public bool HandleJoinIntent(MessageJoin joinIntent)
    {
        logger?.LogDebug("[IntentHandler] HandleJoinIntent: {Node} at LTime {LTime}",
            joinIntent.Node, joinIntent.LTime);

        // Witness the Lamport time
        _clock.Witness(joinIntent.LTime);

        return _memberManager.ExecuteUnderLock(accessor =>
        {
            var memberInfo = accessor.GetMember(joinIntent.Node);

            // Unknown member - create placeholder
            if (memberInfo == null)
            {
                accessor.AddMember(new MemberInfo
                {
                    Name = joinIntent.Node,
                    StateMachine = new MemberStateMachine(
                        joinIntent.Node,
                        MemberStatus.Alive,
                        joinIntent.LTime,
                        logger)
                });

                logger?.LogDebug("[IntentHandler] Created placeholder for {Node}", joinIntent.Node);
                return true; // Rebroadcast new information
            }

            // Check if a message is stale
            if (joinIntent.LTime <= memberInfo.StatusLTime)
            {
                logger?.LogDebug("[IntentHandler] Ignoring stale join intent for {Node}", joinIntent.Node);
                return false;
            }

            // Try state machine transition
            TransitionResult? transitionResult = null;

            accessor.UpdateMember(joinIntent.Node, m =>
            {
                transitionResult = m.StateMachine.TryTransitionOnJoinIntent(joinIntent.LTime);

                // Update Member.Status to match for backward compatibility
                if (transitionResult.WasStateChanged && m.Member != null)
                {
                    m.Member.Status = m.StateMachine.CurrentState;
                }

                if (transitionResult.WasStateChanged)
                {
                    logger?.LogInformation("[IntentHandler] {Reason}", transitionResult.Reason);
                }
                else if (transitionResult.WasLTimeUpdated)
                {
                    logger?.LogDebug("[IntentHandler] {Reason}", transitionResult.Reason);
                }
            });

            // NOTE: handleJoinIntent does NOT emit events in Go
            // Events are emitted by handleNodeJoin (memberlist callback)

            // Decide on rebroadcast
            if (transitionResult?.WasStateChanged == true)
            {
                return true; // Rebroadcast state changes
            }
            
            // Left/Failed members had LTime updated but no state change - don't rebroadcast stale intents
            // For all other cases, rebroadcast
            return transitionResult?.WasLTimeUpdated != true ||
                   memberInfo.Status is not (MemberStatus.Left or MemberStatus.Failed);
            
        });
    }

    /// <summary>
    /// Handles a leave intent message.
    /// Transitions: Alive → Leaving, Failed → Left
    /// Emits: EventMemberLeave when Alive→Leaving (graceful leave) or Failed→Left (removal)
    /// </summary>
    public bool HandleLeaveIntent(MessageLeave leaveIntent)
    {
        logger?.LogDebug("[IntentHandler] HandleLeaveIntent: {Node} at LTime {LTime}",
            leaveIntent.Node, leaveIntent.LTime);

        // Witness the Lamport time
        _clock.Witness(leaveIntent.LTime);

        // Local node refutation: If this is stale, leave intent for the local node while we're alive,
        // broadcast a join intent to refute it
        if (!string.IsNullOrEmpty(_localNodeName) &&
            leaveIntent.Node == _localNodeName &&
            _getSerfState() == SerfState.SerfAlive)
        {
            logger?.LogInformation("[IntentHandler] Refuting stale leave intent for local node");
            // Serf will handle broadcast - we just return false to not rebroadcast the stale leave
            return false;
        }

        _memberManager.ExecuteUnderLock(accessor =>
        {
            var memberInfo = accessor.GetMember(leaveIntent.Node);

            if (memberInfo != null)
            {
                // Don't downgrade Left back to Leaving
                if (memberInfo.Status == MemberStatus.Left)
                {
                    logger?.LogDebug("[IntentHandler] Ignoring leave intent for already-left member {Node}",
                        leaveIntent.Node);
                    return;
                }

                // Capture old status for event emission
                var oldStatus = memberInfo.Status;

                // Try state machine transition
                accessor.UpdateMember(leaveIntent.Node, m =>
                {
                    var result = m.StateMachine.TryTransitionOnLeaveIntent(leaveIntent.LTime);

                    // Update Member.Status to match for backward compatibility
                    if (result.WasStateChanged)
                    {
                        m.Member.Status = m.StateMachine.CurrentState;
                    }

                    if (result.WasStateChanged)
                    {
                        logger?.LogInformation("[IntentHandler] {Reason}", result.Reason);

                        if (oldStatus != MemberStatus.Failed || m.Status != MemberStatus.Left) return;
                        
                        // Emit EventMemberLeave when Failed→Left (per Go implementation)
                        // Note: Alive→Leaving does NOT emit events - events are emitted by NodeEventHandler
                        // when the final Left state is reached via memberlist callback
                        var memberEvent = new MemberEvent
                        {
                            Type = EventType.MemberLeave,
                            Members = [m.Member]
                        };
                        _eventLog.Add(memberEvent);
                    }
                    else if (result.WasLTimeUpdated)
                    {
                        logger?.LogDebug("[IntentHandler] {Reason}", result.Reason);
                    }
                });
            }
            else
            {
                // Node not yet in members - store the intent
                accessor.AddMember(new MemberInfo
                {
                    Name = leaveIntent.Node,
                    StateMachine = new MemberStateMachine(
                        leaveIntent.Node,
                        MemberStatus.Leaving,
                        leaveIntent.LTime,
                        logger)
                });
                logger?.LogDebug("[IntentHandler] Stored leave intent for unknown member: {Node}", leaveIntent.Node);
            }
        });

        return false; // No rebroadcast for now
    }
}

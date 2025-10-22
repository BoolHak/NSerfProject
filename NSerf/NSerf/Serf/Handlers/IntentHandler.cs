// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Logging;
using NSerf.Serf.Events;
using NSerf.Serf.Managers;
using NSerf.Serf.StateMachine;

namespace NSerf.Serf.Handlers;

/// <summary>
/// Handles join and leave intent messages using StateMachine pattern.
/// STATELESS - delegates all state management to MemberManager.
/// </summary>
internal class IntentHandler : IIntentHandler
{
    private readonly IMemberManager _memberManager;
    private readonly List<Event> _eventLog;
    private readonly LamportClock _clock;
    private readonly ILogger? _logger;
    private readonly string _localNodeName;
    private readonly Func<SerfState> _getSerfState;
    private readonly Action<byte[]>? _broadcastJoinIntent;

    public IntentHandler(
        IMemberManager memberManager,
        List<Event> eventLog,
        LamportClock clock,
        ILogger? logger,
        string? localNodeName = null,
        Func<SerfState>? getSerfState = null,
        Action<byte[]>? broadcastJoinIntent = null)
    {
        _memberManager = memberManager ?? throw new ArgumentNullException(nameof(memberManager));
        _eventLog = eventLog ?? throw new ArgumentNullException(nameof(eventLog));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger;
        _localNodeName = localNodeName ?? string.Empty;
        _getSerfState = getSerfState ?? (() => SerfState.SerfAlive);
        _broadcastJoinIntent = broadcastJoinIntent;
    }

    /// <summary>
    /// Handles a join intent message.
    /// CRITICAL: Left/Failed members cannot be resurrected via join intent.
    /// Only Leaving → Alive transition is allowed (refutation).
    /// </summary>
    public bool HandleJoinIntent(MessageJoin joinIntent)
    {
        _logger?.LogDebug("[IntentHandler] HandleJoinIntent: {Node} at LTime {LTime}",
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
                        _logger)
                });

                _logger?.LogDebug("[IntentHandler] Created placeholder for {Node}", joinIntent.Node);
                return true; // Rebroadcast new information
            }

            // Check if message is stale
            if (joinIntent.LTime <= memberInfo.StatusLTime)
            {
                _logger?.LogDebug("[IntentHandler] Ignoring stale join intent for {Node}", joinIntent.Node);
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
                    _logger?.LogInformation("[IntentHandler] {Reason}", transitionResult.Reason);
                }
                else if (transitionResult.WasLTimeUpdated)
                {
                    _logger?.LogDebug("[IntentHandler] {Reason}", transitionResult.Reason);
                }
            });

            // NOTE: handleJoinIntent does NOT emit events in Go
            // Events are emitted by handleNodeJoin (memberlist callback)

            // Decide on rebroadcast
            if (transitionResult?.WasStateChanged == true)
            {
                return true; // Rebroadcast state changes
            }
            else if (transitionResult?.WasLTimeUpdated == true &&
                     (memberInfo.Status == MemberStatus.Left || memberInfo.Status == MemberStatus.Failed))
            {
                // Left/Failed members had LTime updated but no state change - don't rebroadcast stale intents
                return false;
            }

            // For all other cases, rebroadcast
            return true;
        });
    }

    /// <summary>
    /// Handles a leave intent message.
    /// Transitions: Alive → Leaving, Failed → Left
    /// Emits: EventMemberLeave when Failed→Left
    /// </summary>
    public bool HandleLeaveIntent(MessageLeave leaveIntent)
    {
        _logger?.LogDebug("[IntentHandler] HandleLeaveIntent: {Node} at LTime {LTime}",
            leaveIntent.Node, leaveIntent.LTime);

        // Witness the Lamport time
        _clock.Witness(leaveIntent.LTime);

        // Local node refutation: If this is a stale leave intent for the local node while we're alive,
        // broadcast a join intent to refute it
        if (!string.IsNullOrEmpty(_localNodeName) &&
            leaveIntent.Node == _localNodeName &&
            _getSerfState() == SerfState.SerfAlive)
        {
            _logger?.LogInformation("[IntentHandler] Refuting stale leave intent for local node");
            // Broadcast will be handled by Serf - we just return false to not rebroadcast the stale leave
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
                    _logger?.LogDebug("[IntentHandler] Ignoring leave intent for already-left member {Node}",
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
                    if (result.WasStateChanged && m.Member != null)
                    {
                        m.Member.Status = m.StateMachine.CurrentState;
                    }

                    if (result.WasStateChanged)
                    {
                        _logger?.LogInformation("[IntentHandler] {Reason}", result.Reason);

                        // Emit EventMemberLeave when Failed→Left (per Go implementation)
                        if (oldStatus == MemberStatus.Failed && m.Status == MemberStatus.Left && m.Member != null)
                        {
                            var memberEvent = new MemberEvent
                            {
                                Type = EventType.MemberLeave,
                                Members = new List<Member> { m.Member }
                            };
                            _eventLog.Add(memberEvent);
                        }
                    }
                    else if (result.WasLTimeUpdated)
                    {
                        _logger?.LogDebug("[IntentHandler] {Reason}", result.Reason);
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
                        _logger)
                });
                _logger?.LogDebug("[IntentHandler] Stored leave intent for unknown member: {Node}", leaveIntent.Node);
            }
        });

        return false; // No rebroadcast for now
    }
}

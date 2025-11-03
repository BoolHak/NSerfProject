// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Logging;

namespace NSerf.Serf.StateMachine;

/// <summary>
/// Manages member state transitions with guards and validation.
/// Implements the complete state machine for member lifecycle.
/// </summary>
public class MemberStateMachine(
    string nodeName,
    MemberStatus initialState,
    LamportTime initialTime,
    ILogger? logger)
{
    private readonly string _nodeName = nodeName ?? throw new ArgumentNullException(nameof(nodeName));

    public MemberStatus CurrentState => initialState;
    public LamportTime StatusLTime => initialTime;

    // ========== INTENT-BASED TRANSITIONS (Limited Authority) ==========

    /// <summary>
    /// Attempts to transition based on a join intent message.
    /// CRITICAL: Cannot resurrect Left/Failed members.
    /// Only Leaving → Alive is allowed.
    /// </summary>
    public TransitionResult TryTransitionOnJoinIntent(LamportTime intentTime)
    {
        // Guard: Lamport time must be newer
        if (intentTime <= initialTime)
        {
            return TransitionResult.Rejected(
                $"Stale join intent (LTime {intentTime} <= {initialTime})");
        }

        // Update LTime regardless of state change
        var oldLTime = initialTime;
        initialTime = intentTime;

        // CRITICAL: Left/Failed cannot transition to Alive via join intent
        if (initialState == MemberStatus.Left)
        {
            logger?.LogDebug(
                "[StateMachine] {Node}: Join intent blocked - member is Left (LTime updated {Old} → {New})",
                _nodeName, oldLTime, intentTime);

            return TransitionResult.LTimeUpdated(
                initialState, initialState, intentTime,
                "Cannot resurrect Left member via join intent (LTime updated)");
        }

        if (initialState == MemberStatus.Failed)
        {
            logger?.LogDebug(
                "[StateMachine] {Node}: Join intent blocked - member is Failed (LTime updated {Old} → {New})",
                _nodeName, oldLTime, intentTime);

            return TransitionResult.LTimeUpdated(
                initialState, initialState, intentTime,
                "Cannot resurrect Failed member via join intent (LTime updated)");
        }

        // Valid transition: Leaving → Alive (refutation)
        if (initialState == MemberStatus.Leaving)
        {
            var oldState = initialState;
            initialState = MemberStatus.Alive;

            logger?.LogInformation(
                "[StateMachine] {Node}: {Old} → {New} (refutation via join intent, LTime {LTime})",
                _nodeName, oldState, initialState, intentTime);

            return TransitionResult.StateChanged(
                oldState, MemberStatus.Alive, intentTime,
                "Refutation: Leaving → Alive via join intent");
        }

        // Already Alive or None - just LTime update
        return TransitionResult.LTimeUpdated(
            initialState, initialState, intentTime,
            $"Already {initialState}, LTime updated");
    }

    /// <summary>
    /// Attempts to transition based on a leave intent message.
    /// </summary>
    public TransitionResult TryTransitionOnLeaveIntent(LamportTime intentTime)
    {
        // Guard: Lamport time must be newer
        if (intentTime <= initialTime)
        {
            return TransitionResult.Rejected(
                $"Stale leave intent (LTime {intentTime} <= {initialTime})");
        }

        initialTime = intentTime;

        switch (initialState)
        {
            case MemberStatus.Alive:
                initialState = MemberStatus.Leaving;
                logger?.LogInformation(
                    "[StateMachine] {Node}: Alive → Leaving (graceful leave, LTime {LTime})",
                    _nodeName, intentTime);
                return TransitionResult.StateChanged(
                    MemberStatus.Alive, MemberStatus.Leaving, intentTime,
                    "Graceful leave initiated");

            case MemberStatus.Failed:
                initialState = MemberStatus.Left;
                logger?.LogInformation(
                    "[StateMachine] {Node}: Failed → Left (RemoveFailedNode, LTime {LTime})",
                    _nodeName, intentTime);
                return TransitionResult.StateChanged(
                    MemberStatus.Failed, MemberStatus.Left, intentTime,
                    "Failed member marked as Left via leave intent");

            case MemberStatus.Left:
                return TransitionResult.LTimeUpdated(
                    MemberStatus.Left, MemberStatus.Left, intentTime,
                    "Already Left, LTime updated");

            case MemberStatus.Leaving:
                return TransitionResult.LTimeUpdated(
                    MemberStatus.Leaving, MemberStatus.Leaving, intentTime,
                    "Already Leaving, LTime updated");

            default:
                return TransitionResult.NoChange($"No valid transition from {initialState}");
        }
    }

    // ========== AUTHORITATIVE TRANSITIONS (Memberlist) ==========

    /// <summary>
    /// Transition based on memberlist NotifyJoin.
    /// AUTHORITATIVE - always succeeds, can resurrect any state.
    /// </summary>
    public TransitionResult TransitionOnMemberlistJoin()
    {
        var oldState = initialState;

        if (initialState == MemberStatus.Alive)
        {
            return TransitionResult.NoChange("Already Alive (memberlist join)");
        }

        initialState = MemberStatus.Alive;

        logger?.LogInformation(
            "[StateMachine] {Node}: {Old} → Alive (AUTHORITATIVE memberlist join)",
            _nodeName, oldState);

        return TransitionResult.StateChanged(
            oldState, MemberStatus.Alive, initialTime,
            $"Authoritative transition: {oldState} → Alive (memberlist join)");
    }

    /// <summary>
    /// Transition based on memberlist NotifyLeave/NotifyUpdate.
    /// AUTHORITATIVE - always succeeds.
    /// </summary>
    public TransitionResult TransitionOnMemberlistLeave(bool isDead)
    {
        var oldState = initialState;
        var newState = isDead ? MemberStatus.Failed : MemberStatus.Left;

        if (initialState == newState)
        {
            return TransitionResult.NoChange($"Already {newState} (memberlist leave)");
        }

        initialState = newState;

        logger?.LogInformation(
            "[StateMachine] {Node}: {Old} → {New} (AUTHORITATIVE memberlist {Type})",
            _nodeName, oldState, newState, isDead ? "failure" : "leave");

        return TransitionResult.StateChanged(
            oldState, newState, initialTime,
            $"Authoritative: {oldState} → {newState} (memberlist {(isDead ? "failure" : "leave")})");
    }

    /// <summary>
    /// Transition when leave process completes (Leaving → Left).
    /// </summary>
    public TransitionResult TransitionOnLeaveComplete()
    {
        if (initialState != MemberStatus.Leaving)
        {
            return TransitionResult.NoChange($"Not in Leaving state (current: {initialState})");
        }

        initialState = MemberStatus.Left;

        logger?.LogInformation(
            "[StateMachine] {Node}: Leaving → Left (leave complete)",
            _nodeName);

        return TransitionResult.StateChanged(
            MemberStatus.Leaving, MemberStatus.Left, initialTime,
            "Leave process completed");
    }
}

// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Logging;

namespace NSerf.Serf.StateMachine;

/// <summary>
/// Manages member state transitions with guards and validation.
/// Implements the complete state machine for member lifecycle.
/// </summary>
public class MemberStateMachine
{
    private MemberStatus _state;
    private LamportTime _statusLTime;
    private readonly string _nodeName;
    private readonly ILogger? _logger;
    
    public MemberStatus CurrentState => _state;
    public LamportTime StatusLTime => _statusLTime;
    
    public MemberStateMachine(
        string nodeName,
        MemberStatus initialState,
        LamportTime initialTime,
        ILogger? logger)
    {
        _nodeName = nodeName ?? throw new ArgumentNullException(nameof(nodeName));
        _state = initialState;
        _statusLTime = initialTime;
        _logger = logger;
    }
    
    // ========== INTENT-BASED TRANSITIONS (Limited Authority) ==========
    
    /// <summary>
    /// Attempts to transition based on a join intent message.
    /// CRITICAL: Cannot resurrect Left/Failed members.
    /// Only Leaving → Alive is allowed.
    /// </summary>
    public TransitionResult TryTransitionOnJoinIntent(LamportTime intentTime)
    {
        // Guard: Lamport time must be newer
        if (intentTime <= _statusLTime)
        {
            return TransitionResult.Rejected(
                $"Stale join intent (LTime {intentTime} <= {_statusLTime})");
        }
        
        // Update LTime regardless of state change
        var oldLTime = _statusLTime;
        _statusLTime = intentTime;
        
        // CRITICAL: Left/Failed cannot transition to Alive via join intent
        if (_state == MemberStatus.Left)
        {
            _logger?.LogDebug(
                "[StateMachine] {Node}: Join intent blocked - member is Left (LTime updated {Old} → {New})",
                _nodeName, oldLTime, intentTime);
            
            return TransitionResult.LTimeUpdated(
                _state, _state, intentTime,
                "Cannot resurrect Left member via join intent (LTime updated)");
        }
        
        if (_state == MemberStatus.Failed)
        {
            _logger?.LogDebug(
                "[StateMachine] {Node}: Join intent blocked - member is Failed (LTime updated {Old} → {New})",
                _nodeName, oldLTime, intentTime);
            
            return TransitionResult.LTimeUpdated(
                _state, _state, intentTime,
                "Cannot resurrect Failed member via join intent (LTime updated)");
        }
        
        // Valid transition: Leaving → Alive (refutation)
        if (_state == MemberStatus.Leaving)
        {
            var oldState = _state;
            _state = MemberStatus.Alive;
            
            _logger?.LogInformation(
                "[StateMachine] {Node}: {Old} → {New} (refutation via join intent, LTime {LTime})",
                _nodeName, oldState, _state, intentTime);
            
            return TransitionResult.StateChanged(
                oldState, MemberStatus.Alive, intentTime,
                "Refutation: Leaving → Alive via join intent");
        }
        
        // Already Alive or None - just LTime update
        return TransitionResult.LTimeUpdated(
            _state, _state, intentTime,
            $"Already {_state}, LTime updated");
    }
    
    /// <summary>
    /// Attempts to transition based on a leave intent message.
    /// </summary>
    public TransitionResult TryTransitionOnLeaveIntent(LamportTime intentTime)
    {
        // Guard: Lamport time must be newer
        if (intentTime <= _statusLTime)
        {
            return TransitionResult.Rejected(
                $"Stale leave intent (LTime {intentTime} <= {_statusLTime})");
        }
        
        _statusLTime = intentTime;
        
        switch (_state)
        {
            case MemberStatus.Alive:
                _state = MemberStatus.Leaving;
                _logger?.LogInformation(
                    "[StateMachine] {Node}: Alive → Leaving (graceful leave, LTime {LTime})",
                    _nodeName, intentTime);
                return TransitionResult.StateChanged(
                    MemberStatus.Alive, MemberStatus.Leaving, intentTime,
                    "Graceful leave initiated");
            
            case MemberStatus.Failed:
                _state = MemberStatus.Left;
                _logger?.LogInformation(
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
                return TransitionResult.NoChange($"No valid transition from {_state}");
        }
    }
    
    // ========== AUTHORITATIVE TRANSITIONS (Memberlist) ==========
    
    /// <summary>
    /// Transition based on memberlist NotifyJoin.
    /// AUTHORITATIVE - always succeeds, can resurrect any state.
    /// </summary>
    public TransitionResult TransitionOnMemberlistJoin()
    {
        var oldState = _state;
        
        if (_state == MemberStatus.Alive)
        {
            return TransitionResult.NoChange("Already Alive (memberlist join)");
        }
        
        _state = MemberStatus.Alive;
        
        _logger?.LogInformation(
            "[StateMachine] {Node}: {Old} → Alive (AUTHORITATIVE memberlist join)",
            _nodeName, oldState);
        
        return TransitionResult.StateChanged(
            oldState, MemberStatus.Alive, _statusLTime,
            $"Authoritative transition: {oldState} → Alive (memberlist join)");
    }
    
    /// <summary>
    /// Transition based on memberlist NotifyLeave/NotifyUpdate.
    /// AUTHORITATIVE - always succeeds.
    /// </summary>
    public TransitionResult TransitionOnMemberlistLeave(bool isDead)
    {
        var oldState = _state;
        var newState = isDead ? MemberStatus.Failed : MemberStatus.Left;
        
        if (_state == newState)
        {
            return TransitionResult.NoChange($"Already {newState} (memberlist leave)");
        }
        
        _state = newState;
        
        _logger?.LogInformation(
            "[StateMachine] {Node}: {Old} → {New} (AUTHORITATIVE memberlist {Type})",
            _nodeName, oldState, newState, isDead ? "failure" : "leave");
        
        return TransitionResult.StateChanged(
            oldState, newState, _statusLTime,
            $"Authoritative: {oldState} → {newState} (memberlist {(isDead ? "failure" : "leave")})");
    }
    
    /// <summary>
    /// Transition when leave process completes (Leaving → Left).
    /// </summary>
    public TransitionResult TransitionOnLeaveComplete()
    {
        if (_state != MemberStatus.Leaving)
        {
            return TransitionResult.NoChange($"Not in Leaving state (current: {_state})");
        }
        
        _state = MemberStatus.Left;
        
        _logger?.LogInformation(
            "[StateMachine] {Node}: Leaving → Left (leave complete)",
            _nodeName);
        
        return TransitionResult.StateChanged(
            MemberStatus.Leaving, MemberStatus.Left, _statusLTime,
            "Leave process completed");
    }
}

// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

namespace NSerf.Serf.StateMachine;

/// <summary>
/// Represents the type of transition result.
/// </summary>
public enum ResultType
{
    /// <summary>
    /// State actually changed (e.g., Leaving → Alive).
    /// </summary>
    StateChanged,
    
    /// <summary>
    /// Only Lamport time updated, state unchanged (e.g., Left + join intent).
    /// </summary>
    LTimeUpdated,
    
    /// <summary>
    /// No change occurred.
    /// </summary>
    NoChange,
    
    /// <summary>
    /// Transition was rejected (e.g., stale message).
    /// </summary>
    Rejected
}

/// <summary>
/// Result of a state transition attempt.
/// Provides detailed information about what happened during the transition.
/// </summary>
public class TransitionResult
{
    public ResultType Type { get; }
    public MemberStatus? OldState { get; }
    public MemberStatus? NewState { get; }
    public LamportTime? NewLTime { get; }
    public string Reason { get; }
    
    private TransitionResult(
        ResultType type,
        MemberStatus? oldState,
        MemberStatus? newState,
        LamportTime? newLTime,
        string reason)
    {
        Type = type;
        OldState = oldState;
        NewState = newState;
        NewLTime = newLTime;
        Reason = reason;
    }
    
    /// <summary>
    /// Creates a result indicating state was changed.
    /// </summary>
    public static TransitionResult StateChanged(
        MemberStatus oldState,
        MemberStatus newState,
        LamportTime newLTime,
        string reason)
        => new(ResultType.StateChanged, oldState, newState, newLTime, reason);
    
    /// <summary>
    /// Creates a result indicating only LTime was updated (state unchanged).
    /// </summary>
    public static TransitionResult LTimeUpdated(
        MemberStatus oldState,
        MemberStatus newState,
        LamportTime newLTime,
        string reason)
        => new(ResultType.LTimeUpdated, oldState, newState, newLTime, reason);
    
    /// <summary>
    /// Creates a result indicating no change occurred.
    /// </summary>
    public static TransitionResult NoChange(string reason)
        => new(ResultType.NoChange, null, null, null, reason);
    
    /// <summary>
    /// Creates a result indicating transition was rejected.
    /// </summary>
    public static TransitionResult Rejected(string reason)
        => new(ResultType.Rejected, null, null, null, reason);
    
    /// <summary>
    /// True if state actually changed.
    /// </summary>
    public bool WasStateChanged => Type == ResultType.StateChanged;
    
    /// <summary>
    /// True if LTime was updated (either with or without state change).
    /// </summary>
    public bool WasLTimeUpdated => Type == ResultType.LTimeUpdated || Type == ResultType.StateChanged;
    
    /// <summary>
    /// True if transition was rejected.
    /// </summary>
    public bool WasRejected => Type == ResultType.Rejected;
}

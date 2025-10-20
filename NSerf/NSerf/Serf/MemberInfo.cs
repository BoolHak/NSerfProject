namespace NSerf.Serf;
/// <summary>
/// Internal member state tracking for Serf.
/// Tracks member status and Lamport time for state changes.
/// </summary>
internal class MemberInfo
{
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// State machine managing member status transitions.
    /// Required - all MemberInfo instances must initialize this.
    /// </summary>
    public StateMachine.MemberStateMachine StateMachine { get; set; } = null!;

    /// <summary>
    /// Current status of the member - delegates to StateMachine.
    /// </summary>
    public MemberStatus Status => StateMachine.CurrentState;

    /// <summary>
    /// Lamport time of last status update - delegates to StateMachine.
    /// </summary>
    public LamportTime StatusLTime => StateMachine.StatusLTime;

    public DateTimeOffset LeaveTime { get; set; }
    public Member Member { get; set; } = new Member();
}

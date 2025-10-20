using Xunit;
using NSerf.Serf;
using NSerf.Serf.StateMachine;

namespace NSerfTests.Serf.StateMachine;

/// <summary>
/// Tests for MemberStateMachine - Complete state transition validation.
/// Following TDD: These tests are written FIRST and should FAIL until implementation is complete.
/// </summary>
public class MemberStateMachineTests
{
    // ========== TransitionResult Tests ==========

    [Fact]
    public void TransitionResult_StateChanged_HasCorrectProperties()
    {
        // Arrange & Act
        var result = TransitionResult.StateChanged(
            MemberStatus.Alive, 
            MemberStatus.Leaving, 
            100, 
            "Graceful leave");

        // Assert
        Assert.Equal(ResultType.StateChanged, result.Type);
        Assert.True(result.WasStateChanged);
        Assert.False(result.WasRejected);
        Assert.Equal(MemberStatus.Alive, result.OldState);
        Assert.Equal(MemberStatus.Leaving, result.NewState);
        Assert.Equal(100UL, result.NewLTime);
        Assert.Equal("Graceful leave", result.Reason);
    }

    [Fact]
    public void TransitionResult_LTimeUpdated_HasCorrectProperties()
    {
        // Arrange & Act
        var result = TransitionResult.LTimeUpdated(
            MemberStatus.Left,
            MemberStatus.Left,
            200,
            "LTime updated but state blocked");

        // Assert
        Assert.Equal(ResultType.LTimeUpdated, result.Type);
        Assert.False(result.WasStateChanged);
        Assert.True(result.WasLTimeUpdated);
        Assert.False(result.WasRejected);
        Assert.Equal(200UL, result.NewLTime);
    }

    [Fact]
    public void TransitionResult_Rejected_HasCorrectProperties()
    {
        // Arrange & Act
        var result = TransitionResult.Rejected("Stale message");

        // Assert
        Assert.Equal(ResultType.Rejected, result.Type);
        Assert.True(result.WasRejected);
        Assert.False(result.WasStateChanged);
        Assert.False(result.WasLTimeUpdated);
        Assert.Equal("Stale message", result.Reason);
    }

    [Fact]
    public void TransitionResult_NoChange_HasCorrectProperties()
    {
        // Arrange & Act
        var result = TransitionResult.NoChange("Already in correct state");

        // Assert
        Assert.Equal(ResultType.NoChange, result.Type);
        Assert.False(result.WasStateChanged);
        Assert.False(result.WasRejected);
    }

    // ========== Stale Message Rejection Tests ==========

    [Fact]
    public void JoinIntent_WithStaleLTime_IsRejected()
    {
        // Arrange
        var sm = new MemberStateMachine("node1", MemberStatus.Alive, new LamportTime(100), null);

        // Act
        var result = sm.TryTransitionOnJoinIntent(50); // 50 < 100 (stale)

        // Assert
        Assert.True(result.WasRejected);
        Assert.Equal(MemberStatus.Alive, sm.CurrentState);
        Assert.Equal(new LamportTime(100), sm.StatusLTime); // Unchanged
    }

    [Fact]
    public void LeaveIntent_WithStaleLTime_IsRejected()
    {
        // Arrange
        var sm = new MemberStateMachine("node1", MemberStatus.Alive, new LamportTime(100), null);

        // Act
        var result = sm.TryTransitionOnLeaveIntent(50); // Stale

        // Assert
        Assert.True(result.WasRejected);
        Assert.Equal(MemberStatus.Alive, sm.CurrentState);
        Assert.Equal(new LamportTime(100), sm.StatusLTime);
    }

    [Fact]
    public void JoinIntent_WithEqualLTime_IsRejected()
    {
        // Arrange
        var sm = new MemberStateMachine("node1", MemberStatus.Alive, new LamportTime(100), null);

        // Act
        var result = sm.TryTransitionOnJoinIntent(100); // Equal, not newer

        // Assert
        Assert.True(result.WasRejected);
    }

    [Fact]
    public void LeaveIntent_WithEqualLTime_IsRejected()
    {
        // Arrange
        var sm = new MemberStateMachine("node1", MemberStatus.Alive, new LamportTime(100), null);

        // Act
        var result = sm.TryTransitionOnLeaveIntent(100);

        // Assert
        Assert.True(result.WasRejected);
    }

    // ========== CRITICAL: Auto-Rejoin Logic Tests ==========

    [Fact]
    public void JoinIntent_LeftMember_BlocksStateChange_ButUpdatesLTime()
    {
        // Arrange
        var sm = new MemberStateMachine("node1", MemberStatus.Left, new LamportTime(100), null);

        // Act
        var result = sm.TryTransitionOnJoinIntent(200); // Newer time

        // Assert - CRITICAL
        Assert.False(result.WasStateChanged); // State should NOT change
        Assert.True(result.WasLTimeUpdated); // LTime SHOULD update
        Assert.Equal(MemberStatus.Left, sm.CurrentState); // Should remain Left
        Assert.Equal(new LamportTime(200), sm.StatusLTime); // LTime should be updated
        Assert.Contains("Left", result.Reason); // Should indicate Left member resurrection blocked
    }

    [Fact]
    public void JoinIntent_FailedMember_BlocksStateChange_ButUpdatesLTime()
    {
        // Arrange
        var sm = new MemberStateMachine("node1", MemberStatus.Failed, new LamportTime(100), null);

        // Act
        var result = sm.TryTransitionOnJoinIntent(200); // Newer time

        // Assert - CRITICAL
        Assert.False(result.WasStateChanged); // State should NOT change
        Assert.True(result.WasLTimeUpdated); // LTime SHOULD update
        Assert.Equal(MemberStatus.Failed, sm.CurrentState); // Should remain Failed
        Assert.Equal(new LamportTime(200), sm.StatusLTime); // LTime should be updated
        Assert.Contains("Failed", result.Reason); // Should indicate Failed member resurrection blocked
    }

    // ========== Valid Refutation Tests ==========

    [Fact]
    public void JoinIntent_LeavingMember_TransitionsToAlive()
    {
        // Arrange
        var sm = new MemberStateMachine("node1", MemberStatus.Leaving, new LamportTime(100), null);

        // Act
        var result = sm.TryTransitionOnJoinIntent(200);

        // Assert
        Assert.True(result.WasStateChanged); // State should change
        Assert.Equal(MemberStatus.Leaving, result.OldState);
        Assert.Equal(MemberStatus.Alive, result.NewState);
        Assert.Equal(MemberStatus.Alive, sm.CurrentState);
        Assert.Equal(new LamportTime(200), sm.StatusLTime);
    }

    [Fact]
    public void JoinIntent_AliveMember_UpdatesLTimeOnly()
    {
        // Arrange
        var sm = new MemberStateMachine("node1", MemberStatus.Alive, new LamportTime(100), null);

        // Act
        var result = sm.TryTransitionOnJoinIntent(200);

        // Assert
        Assert.False(result.WasStateChanged);
        Assert.True(result.WasLTimeUpdated);
        Assert.Equal(MemberStatus.Alive, sm.CurrentState);
        Assert.Equal(new LamportTime(200), sm.StatusLTime);
    }

    // ========== Leave Intent Transition Tests ==========

    [Fact]
    public void LeaveIntent_AliveMember_TransitionsToLeaving()
    {
        // Arrange
        var sm = new MemberStateMachine("node1", MemberStatus.Alive, new LamportTime(100), null);

        // Act
        var result = sm.TryTransitionOnLeaveIntent(200);

        // Assert
        Assert.True(result.WasStateChanged);
        Assert.Equal(MemberStatus.Alive, result.OldState);
        Assert.Equal(MemberStatus.Leaving, result.NewState);
        Assert.Equal(MemberStatus.Leaving, sm.CurrentState);
        Assert.Equal(new LamportTime(200), sm.StatusLTime);
    }

    [Fact]
    public void LeaveIntent_FailedMember_TransitionsToLeft()
    {
        // Arrange
        var sm = new MemberStateMachine("node1", MemberStatus.Failed, new LamportTime(100), null);

        // Act
        var result = sm.TryTransitionOnLeaveIntent(200);

        // Assert
        Assert.True(result.WasStateChanged);
        Assert.Equal(MemberStatus.Failed, result.OldState);
        Assert.Equal(MemberStatus.Left, result.NewState);
        Assert.Equal(MemberStatus.Left, sm.CurrentState);
    }

    [Fact]
    public void LeaveIntent_LeftMember_UpdatesLTimeOnly()
    {
        // Arrange
        var sm = new MemberStateMachine("node1", MemberStatus.Left, new LamportTime(100), null);

        // Act
        var result = sm.TryTransitionOnLeaveIntent(200);

        // Assert
        Assert.False(result.WasStateChanged);
        Assert.True(result.WasLTimeUpdated);
        Assert.Equal(MemberStatus.Left, sm.CurrentState);
        Assert.Equal(new LamportTime(200), sm.StatusLTime);
    }

    [Fact]
    public void LeaveIntent_LeavingMember_UpdatesLTimeOnly()
    {
        // Arrange
        var sm = new MemberStateMachine("node1", MemberStatus.Leaving, new LamportTime(100), null);

        // Act
        var result = sm.TryTransitionOnLeaveIntent(200);

        // Assert
        Assert.False(result.WasStateChanged);
        Assert.True(result.WasLTimeUpdated);
        Assert.Equal(MemberStatus.Leaving, sm.CurrentState);
    }

    // ========== AUTHORITATIVE Memberlist Transition Tests ==========

    [Fact]
    public void MemberlistJoin_FromLeft_AlwaysSucceeds()
    {
        // Arrange
        var sm = new MemberStateMachine("node1", MemberStatus.Left, new LamportTime(100), null);

        // Act
        var result = sm.TransitionOnMemberlistJoin();

        // Assert
        Assert.True(result.WasStateChanged);
        Assert.Equal(MemberStatus.Left, result.OldState);
        Assert.Equal(MemberStatus.Alive, result.NewState);
        Assert.Equal(MemberStatus.Alive, sm.CurrentState);
        Assert.Contains("authoritative", result.Reason.ToLower()); // Should indicate authoritative transition
    }

    [Fact]
    public void MemberlistJoin_FromFailed_AlwaysSucceeds()
    {
        // Arrange
        var sm = new MemberStateMachine("node1", MemberStatus.Failed, new LamportTime(100), null);

        // Act
        var result = sm.TransitionOnMemberlistJoin();

        // Assert
        Assert.True(result.WasStateChanged);
        Assert.Equal(MemberStatus.Failed, result.OldState);
        Assert.Equal(MemberStatus.Alive, result.NewState);
        Assert.Equal(MemberStatus.Alive, sm.CurrentState);
    }

    [Fact]
    public void MemberlistJoin_FromLeaving_AlwaysSucceeds()
    {
        // Arrange
        var sm = new MemberStateMachine("node1", MemberStatus.Leaving, new LamportTime(100), null);

        // Act
        var result = sm.TransitionOnMemberlistJoin();

        // Assert
        Assert.True(result.WasStateChanged);
        Assert.Equal(MemberStatus.Alive, sm.CurrentState);
    }

    [Fact]
    public void MemberlistJoin_FromAlive_NoChange()
    {
        // Arrange
        var sm = new MemberStateMachine("node1", MemberStatus.Alive, new LamportTime(100), null);

        // Act
        var result = sm.TransitionOnMemberlistJoin();

        // Assert
        Assert.False(result.WasStateChanged);
        Assert.Equal(MemberStatus.Alive, sm.CurrentState);
    }

    [Fact]
    public void MemberlistLeave_Dead_TransitionsToFailed()
    {
        // Arrange
        var sm = new MemberStateMachine("node1", MemberStatus.Alive, new LamportTime(100), null);

        // Act
        var result = sm.TransitionOnMemberlistLeave(isDead: true);

        // Assert
        Assert.True(result.WasStateChanged);
        Assert.Equal(MemberStatus.Alive, result.OldState);
        Assert.Equal(MemberStatus.Failed, result.NewState);
        Assert.Equal(MemberStatus.Failed, sm.CurrentState);
    }

    [Fact]
    public void MemberlistLeave_Graceful_TransitionsToLeft()
    {
        // Arrange
        var sm = new MemberStateMachine("node1", MemberStatus.Alive, new LamportTime(100), null);

        // Act
        var result = sm.TransitionOnMemberlistLeave(isDead: false);

        // Assert
        Assert.True(result.WasStateChanged);
        Assert.Equal(MemberStatus.Alive, result.OldState);
        Assert.Equal(MemberStatus.Left, result.NewState);
        Assert.Equal(MemberStatus.Left, sm.CurrentState);
    }

    // ========== Edge Case Tests ==========

    [Fact]
    public void LeaveComplete_FromLeaving_TransitionsToLeft()
    {
        // Arrange
        var sm = new MemberStateMachine("node1", MemberStatus.Leaving, new LamportTime(100), null);

        // Act
        var result = sm.TransitionOnLeaveComplete();

        // Assert
        Assert.True(result.WasStateChanged);
        Assert.Equal(MemberStatus.Leaving, result.OldState);
        Assert.Equal(MemberStatus.Left, result.NewState);
        Assert.Equal(MemberStatus.Left, sm.CurrentState);
    }

    [Fact]
    public void LeaveComplete_FromNonLeaving_NoChange()
    {
        // Arrange
        var sm = new MemberStateMachine("node1", MemberStatus.Alive, new LamportTime(100), null);

        // Act
        var result = sm.TransitionOnLeaveComplete();

        // Assert
        Assert.False(result.WasStateChanged);
        Assert.Equal(MemberStatus.Alive, sm.CurrentState);
    }

    [Fact]
    public void MultipleTransitions_MaintainCorrectLTime()
    {
        // Arrange
        var sm = new MemberStateMachine("node1", MemberStatus.Alive, new LamportTime(100), null);

        // Act & Assert - Multiple transitions
        var result1 = sm.TryTransitionOnLeaveIntent(150);
        Assert.Equal(new LamportTime(150), sm.StatusLTime);
        Assert.Equal(MemberStatus.Leaving, sm.CurrentState);

        var result2 = sm.TryTransitionOnJoinIntent(200);
        Assert.Equal(new LamportTime(200), sm.StatusLTime);
        Assert.Equal(MemberStatus.Alive, sm.CurrentState);

        var result3 = sm.TryTransitionOnJoinIntent(180); // Stale
        Assert.Equal(new LamportTime(200), sm.StatusLTime); // Should not change
        Assert.True(result3.WasRejected);
    }

    [Fact]
    public void Constructor_SetsInitialStateAndTime()
    {
        // Act
        var sm = new MemberStateMachine("test-node", MemberStatus.Alive, new LamportTime(42), null);

        // Assert
        Assert.Equal(MemberStatus.Alive, sm.CurrentState);
        Assert.Equal(new LamportTime(42), sm.StatusLTime);
    }
}

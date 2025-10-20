// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSerf.Serf;
using NSerf.Serf.Events;
using NSerf.Serf.Handlers;
using NSerf.Serf.Managers;
using NSerf.Serf.StateMachine;
using Xunit;

namespace NSerfTests.Serf.Handlers;

/// <summary>
/// Tests for IntentHandler - handles join/leave intent messages.
/// Phase 3: Critical tests for auto-rejoin logic and state transitions.
/// </summary>
public class IntentHandlerTests
{
    /// <summary>
    /// CRITICAL TEST: Left member receives join intent with newer LTime.
    /// Expected: State remains Left, LTime is updated.
    /// This prevents stale join intents from resurrecting dead nodes.
    /// </summary>
    [Fact]
    public void HandleJoinIntent_LeftMember_BlocksResurrection_UpdatesLTime()
    {
        // Arrange
        var memberManager = new MemberManager();
        var eventLog = new List<Event>();
        var clock = new LamportClock();
        clock.Witness(100);
        
        var handler = new IntentHandler(memberManager, eventLog, clock, null);
        
        // Add member in Left state
        memberManager.ExecuteUnderLock(accessor =>
        {
            accessor.AddMember(new MemberInfo
            {
                Name = "node1",
                StateMachine = new MemberStateMachine("node1", MemberStatus.Left, 100, null)
            });
        });
        
        var joinIntent = new MessageJoin
        {
            LTime = 200,
            Node = "node1"
        };
        
        // Act
        var shouldRebroadcast = handler.HandleJoinIntent(joinIntent);
        
        // Assert
        shouldRebroadcast.Should().BeFalse("stale join intents for Left members should not be rebroadcast");
        
        var member = memberManager.ExecuteUnderLock(accessor => accessor.GetMember("node1"));
        member.Should().NotBeNull();
        member!.Status.Should().Be(MemberStatus.Left, "Left members cannot be resurrected via join intent");
        member.StatusLTime.Should().Be(200, "LTime should be updated even when state doesn't change");
        
        eventLog.Should().BeEmpty("handleJoinIntent never emits events - events come from handleNodeJoin");
    }
    
    /// <summary>
    /// CRITICAL TEST: Failed member receives join intent with newer LTime.
    /// Expected: State remains Failed, LTime is updated.
    /// </summary>
    [Fact]
    public void HandleJoinIntent_FailedMember_BlocksResurrection_UpdatesLTime()
    {
        // Arrange
        var memberManager = new MemberManager();
        var eventLog = new List<Event>();
        var clock = new LamportClock();
        
        var handler = new IntentHandler(memberManager, eventLog, clock, null);
        
        // Add member in Failed state
        memberManager.ExecuteUnderLock(accessor =>
        {
            accessor.AddMember(new MemberInfo
            {
                Name = "node1",
                StateMachine = new MemberStateMachine("node1", MemberStatus.Failed, 100, null)
            });
        });
        
        var joinIntent = new MessageJoin
        {
            LTime = 200,
            Node = "node1"
        };
        
        // Act
        var shouldRebroadcast = handler.HandleJoinIntent(joinIntent);
        
        // Assert
        shouldRebroadcast.Should().BeFalse("stale join intents for Failed members should not be rebroadcast");
        
        var member = memberManager.ExecuteUnderLock(accessor => accessor.GetMember("node1"));
        member.Should().NotBeNull();
        member!.Status.Should().Be(MemberStatus.Failed, "Failed members cannot be resurrected via join intent");
        member.StatusLTime.Should().Be(200, "LTime should be updated even when state doesn't change");
        
        eventLog.Should().BeEmpty("handleJoinIntent never emits events - events come from handleNodeJoin");
    }
    
    /// <summary>
    /// Test: Leaving member receives join intent (refutation scenario).
    /// Expected: State changes to Alive, event is emitted.
    /// </summary>
    [Fact]
    public void HandleJoinIntent_LeavingMember_AllowsRefutation_EmitsEvent()
    {
        // Arrange
        var memberManager = new MemberManager();
        var eventLog = new List<Event>();
        var clock = new LamportClock();
        
        var handler = new IntentHandler(memberManager, eventLog, clock, null);
        
        // Add member in Leaving state
        memberManager.ExecuteUnderLock(accessor =>
        {
            accessor.AddMember(new MemberInfo
            {
                Name = "node1",
                StateMachine = new MemberStateMachine("node1", MemberStatus.Leaving, 100, null),
                Member = new Member { Name = "node1", Status = MemberStatus.Leaving }
            });
        });
        
        var joinIntent = new MessageJoin
        {
            LTime = 200,
            Node = "node1"
        };
        
        // Act
        var shouldRebroadcast = handler.HandleJoinIntent(joinIntent);
        
        // Assert
        shouldRebroadcast.Should().BeTrue("refutation should be rebroadcast");
        
        var member = memberManager.ExecuteUnderLock(accessor => accessor.GetMember("node1"));
        member.Should().NotBeNull();
        member!.Status.Should().Be(MemberStatus.Alive, "Leaving members can refute and return to Alive");
        member.StatusLTime.Should().Be(200);
        
        eventLog.Should().BeEmpty("handleJoinIntent does NOT emit events - events are emitted by handleNodeJoin");
    }
    
    /// <summary>
    /// Test: Stale join intent (older LTime) should be rejected.
    /// </summary>
    [Fact]
    public void HandleJoinIntent_StaleMessage_IsRejected()
    {
        // Arrange
        var memberManager = new MemberManager();
        var eventLog = new List<Event>();
        var clock = new LamportClock();
        
        var handler = new IntentHandler(memberManager, eventLog, clock, null);
        
        // Add member with LTime 200
        memberManager.ExecuteUnderLock(accessor =>
        {
            accessor.AddMember(new MemberInfo
            {
                Name = "node1",
                StateMachine = new MemberStateMachine("node1", MemberStatus.Alive, 200, null)
            });
        });
        
        var staleJoinIntent = new MessageJoin
        {
            LTime = 100,  // Older than current LTime
            Node = "node1"
        };
        
        // Act
        var shouldRebroadcast = handler.HandleJoinIntent(staleJoinIntent);
        
        // Assert
        shouldRebroadcast.Should().BeFalse("stale messages should not be rebroadcast");
        
        var member = memberManager.ExecuteUnderLock(accessor => accessor.GetMember("node1"));
        member.Should().NotBeNull();
        member!.StatusLTime.Should().Be(200, "LTime should not be downgraded by stale messages");
    }
    
    /// <summary>
    /// Test: Unknown member receives join intent - placeholder is created.
    /// </summary>
    [Fact]
    public void HandleJoinIntent_UnknownMember_CreatesPlaceholder()
    {
        // Arrange
        var memberManager = new MemberManager();
        var eventLog = new List<Event>();
        var clock = new LamportClock();
        
        var handler = new IntentHandler(memberManager, eventLog, clock, null);
        
        var joinIntent = new MessageJoin
        {
            LTime = 100,
            Node = "new-node"
        };
        
        // Act
        var shouldRebroadcast = handler.HandleJoinIntent(joinIntent);
        
        // Assert
        shouldRebroadcast.Should().BeTrue("new member information should be rebroadcast");
        
        var member = memberManager.ExecuteUnderLock(accessor => accessor.GetMember("new-node"));
        member.Should().NotBeNull();
        member!.Name.Should().Be("new-node");
        member.Status.Should().Be(MemberStatus.Alive);
        member.StatusLTime.Should().Be(100);
        
        eventLog.Should().BeEmpty("handleJoinIntent never emits events - events come from HandleNodeJoin");
    }
    
    // ==================== EDGE CASES ====================
    
    /// <summary>
    /// EDGE CASE: LTime equals statusLTime (not just less than).
    /// Per Go: message is considered old and ignored.
    /// </summary>
    [Fact]
    public void HandleJoinIntent_EqualLTime_IsRejected()
    {
        // Arrange
        var memberManager = new MemberManager();
        var eventLog = new List<Event>();
        var clock = new LamportClock();
        
        var handler = new IntentHandler(memberManager, eventLog, clock, null);
        
        // Add member with LTime 100
        memberManager.ExecuteUnderLock(accessor =>
        {
            accessor.AddMember(new MemberInfo
            {
                Name = "node1",
                StateMachine = new MemberStateMachine("node1", MemberStatus.Alive, 100, null)
            });
        });
        
        var equalJoinIntent = new MessageJoin
        {
            LTime = 100,  // Equal to current LTime
            Node = "node1"
        };
        
        // Act
        var shouldRebroadcast = handler.HandleJoinIntent(equalJoinIntent);
        
        // Assert
        shouldRebroadcast.Should().BeFalse("equal LTime messages are considered old");
        
        var member = memberManager.ExecuteUnderLock(accessor => accessor.GetMember("node1"));
        member!.StatusLTime.Should().Be(100, "LTime should not change for equal LTime messages");
    }
    
    /// <summary>
    /// EDGE CASE: HandleLeaveIntent for Failed->Left transition.
    /// Per Go: Emits EventMemberLeave.
    /// </summary>
    [Fact]
    public void HandleLeaveIntent_FailedToLeft_EmitsEvent()
    {
        // Arrange
        var memberManager = new MemberManager();
        var eventLog = new List<Event>();
        var clock = new LamportClock();
        
        var handler = new IntentHandler(memberManager, eventLog, clock, null);
        
        // Add member in Failed state
        memberManager.ExecuteUnderLock(accessor =>
        {
            accessor.AddMember(new MemberInfo
            {
                Name = "node1",
                StateMachine = new MemberStateMachine("node1", MemberStatus.Failed, 100, null),
                Member = new Member { Name = "node1", Status = MemberStatus.Failed }
            });
        });
        
        var leaveIntent = new MessageLeave
        {
            LTime = 200,
            Node = "node1"
        };
        
        // Act
        handler.HandleLeaveIntent(leaveIntent);
        
        // Assert
        var member = memberManager.ExecuteUnderLock(accessor => accessor.GetMember("node1"));
        member!.Status.Should().Be(MemberStatus.Left, "Failed should transition to Left");
        
        eventLog.Should().HaveCount(1, "EventMemberLeave should be emitted for Failed->Left");
        eventLog[0].Should().BeOfType<MemberEvent>();
        var memberEvent = (MemberEvent)eventLog[0];
        memberEvent.Type.Should().Be(EventType.MemberLeave);
    }
    
    /// <summary>
    /// EDGE CASE: Alive member receives leave intent -> Leaving.
    /// </summary>
    [Fact]
    public void HandleLeaveIntent_AliveToLeaving_Works()
    {
        // Arrange
        var memberManager = new MemberManager();
        var eventLog = new List<Event>();
        var clock = new LamportClock();
        
        var handler = new IntentHandler(memberManager, eventLog, clock, null);
        
        // Add member in Alive state
        memberManager.ExecuteUnderLock(accessor =>
        {
            accessor.AddMember(new MemberInfo
            {
                Name = "node1",
                StateMachine = new MemberStateMachine("node1", MemberStatus.Alive, 100, null),
                Member = new Member { Name = "node1", Status = MemberStatus.Alive }
            });
        });
        
        var leaveIntent = new MessageLeave
        {
            LTime = 200,
            Node = "node1"
        };
        
        // Act
        handler.HandleLeaveIntent(leaveIntent);
        
        // Assert
        var member = memberManager.ExecuteUnderLock(accessor => accessor.GetMember("node1"));
        member!.Status.Should().Be(MemberStatus.Leaving, "Alive should transition to Leaving");
        member.StatusLTime.Should().Be(200);
        
        eventLog.Should().BeEmpty("Alive->Leaving does NOT emit events");
    }
    
    /// <summary>
    /// EDGE CASE: Leaving member receives another leave intent.
    /// Per Go: Status remains unchanged.
    /// </summary>
    [Fact]
    public void HandleLeaveIntent_LeavingMember_StatusUnchanged()
    {
        // Arrange
        var memberManager = new MemberManager();
        var eventLog = new List<Event>();
        var clock = new LamportClock();
        
        var handler = new IntentHandler(memberManager, eventLog, clock, null);
        
        // Add member in Leaving state
        memberManager.ExecuteUnderLock(accessor =>
        {
            accessor.AddMember(new MemberInfo
            {
                Name = "node1",
                StateMachine = new MemberStateMachine("node1", MemberStatus.Leaving, 100, null)
            });
        });
        
        var leaveIntent = new MessageLeave
        {
            LTime = 200,
            Node = "node1"
        };
        
        // Act
        handler.HandleLeaveIntent(leaveIntent);
        
        // Assert
        var member = memberManager.ExecuteUnderLock(accessor => accessor.GetMember("node1"));
        member!.Status.Should().Be(MemberStatus.Leaving, "Leaving status should remain unchanged");
        member.StatusLTime.Should().Be(200, "LTime should still be updated");
    }
    
    /// <summary>
    /// EDGE CASE: Left member receives leave intent.
    /// Per Go: Ignored, status unchanged.
    /// </summary>
    [Fact]
    public void HandleLeaveIntent_LeftMember_IsIgnored()
    {
        // Arrange
        var memberManager = new MemberManager();
        var eventLog = new List<Event>();
        var clock = new LamportClock();
        
        var handler = new IntentHandler(memberManager, eventLog, clock, null);
        
        // Add member in Left state
        memberManager.ExecuteUnderLock(accessor =>
        {
            accessor.AddMember(new MemberInfo
            {
                Name = "node1",
                StateMachine = new MemberStateMachine("node1", MemberStatus.Left, 100, null)
            });
        });
        
        var leaveIntent = new MessageLeave
        {
            LTime = 200,
            Node = "node1"
        };
        
        // Act
        handler.HandleLeaveIntent(leaveIntent);
        
        // Assert
        var member = memberManager.ExecuteUnderLock(accessor => accessor.GetMember("node1"));
        member!.Status.Should().Be(MemberStatus.Left, "Left status should remain unchanged");
        member.StatusLTime.Should().Be(100, "LTime should NOT be updated for already-left members");
    }
    
    /// <summary>
    /// EDGE CASE: Stale leave intent (equal LTime).
    /// Per Go: Ignored to prevent infinite rebroadcasts.
    /// </summary>
    [Fact]
    public void HandleLeaveIntent_EqualLTime_IsIgnored()
    {
        // Arrange
        var memberManager = new MemberManager();
        var eventLog = new List<Event>();
        var clock = new LamportClock();
        
        var handler = new IntentHandler(memberManager, eventLog, clock, null);
        
        // Add member with LTime 100
        memberManager.ExecuteUnderLock(accessor =>
        {
            accessor.AddMember(new MemberInfo
            {
                Name = "node1",
                StateMachine = new MemberStateMachine("node1", MemberStatus.Alive, 100, null)
            });
        });
        
        var staleLeaveIntent = new MessageLeave
        {
            LTime = 100,  // Equal to current LTime
            Node = "node1"
        };
        
        // Act
        handler.HandleLeaveIntent(staleLeaveIntent);
        
        // Assert
        var member = memberManager.ExecuteUnderLock(accessor => accessor.GetMember("node1"));
        member!.Status.Should().Be(MemberStatus.Alive, "Status should remain unchanged");
        member.StatusLTime.Should().Be(100, "LTime should not change");
    }
    
    /// <summary>
    /// EDGE CASE: Local node receives leave intent for itself while Alive.
    /// Per Go: Refutes by returning false (Serf broadcasts join intent separately).
    /// </summary>
    [Fact]
    public void HandleLeaveIntent_LocalNodeWhileAlive_RefutesStaleLeave()
    {
        // Arrange
        var memberManager = new MemberManager();
        var eventLog = new List<Event>();
        var clock = new LamportClock();
        
        var handler = new IntentHandler(
            memberManager, 
            eventLog, 
            clock, 
            null,
            localNodeName: "local-node",
            getSerfState: () => SerfState.SerfAlive);
        
        var leaveIntent = new MessageLeave
        {
            LTime = 100,
            Node = "local-node"  // Leave intent for local node
        };
        
        // Act
        var shouldRebroadcast = handler.HandleLeaveIntent(leaveIntent);
        
        // Assert
        shouldRebroadcast.Should().BeFalse("stale leave intent for local node should not be rebroadcast");
    }
    
    /// <summary>
    /// EDGE CASE: Unknown member receives leave intent.
    /// Per Go: Intent is buffered (placeholder created).
    /// </summary>
    [Fact]
    public void HandleLeaveIntent_UnknownMember_CreatesPlaceholder()
    {
        // Arrange
        var memberManager = new MemberManager();
        var eventLog = new List<Event>();
        var clock = new LamportClock();
        
        var handler = new IntentHandler(memberManager, eventLog, clock, null);
        
        var leaveIntent = new MessageLeave
        {
            LTime = 100,
            Node = "unknown-node"
        };
        
        // Act
        handler.HandleLeaveIntent(leaveIntent);
        
        // Assert
        var member = memberManager.ExecuteUnderLock(accessor => accessor.GetMember("unknown-node"));
        member.Should().NotBeNull("placeholder should be created for unknown node");
        member!.Status.Should().Be(MemberStatus.Leaving, "placeholder should be in Leaving state");
        member.StatusLTime.Should().Be(100);
    }
}

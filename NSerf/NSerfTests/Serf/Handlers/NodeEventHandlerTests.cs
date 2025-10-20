using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSerf.Memberlist.State;
using NSerf.Serf;
using NSerf.Serf.Events;
using NSerf.Serf.Handlers;
using NSerf.Serf.Managers;
using NSerf.Serf.StateMachine;
using Xunit;

namespace NSerfTests.Serf.Handlers;

/// <summary>
/// Phase 4 TDD Tests: NodeEventHandler
/// Tests authoritative memberlist callbacks (NotifyJoin/NotifyLeave).
/// 
/// Key Principle: NodeEventHandler is AUTHORITATIVE
/// - CAN resurrect Left/Failed members (unlike IntentHandler)
/// - ALWAYS emits events (unlike IntentHandler which doesn't emit for joins)
/// </summary>
public class NodeEventHandlerTests
{
    // ==================== HANDLE NODE JOIN TESTS ====================
    
    /// <summary>
    /// Test: HandleNodeJoin creates a new member when it doesn't exist.
    /// Expected: Member created with Alive status, event emitted.
    /// </summary>
    [Fact]
    public void HandleNodeJoin_NewMember_CreatesAndEmitsEvent()
    {
        // Arrange
        var memberManager = new MemberManager();
        var eventLog = new List<Event>();
        var clock = new LamportClock();
        
        var handler = new NodeEventHandler(memberManager, eventLog, clock, null, null);
        
        var node = new Node
        {
            Name = "node1",
            Addr = IPAddress.Parse("192.168.1.1"),
            Port = 7946,
            Meta = []
        };
        
        // Act
        handler.HandleNodeJoin(node);
        
        // Assert
        var member = memberManager.ExecuteUnderLock(accessor => accessor.GetMember("node1"));
        member.Should().NotBeNull();
        member!.Status.Should().Be(MemberStatus.Alive);
        member.Member.Should().NotBeNull();
        member.Member!.Name.Should().Be("node1");
        member.Member.Addr.Should().Be(IPAddress.Parse("192.168.1.1"));
        member.Member.Port.Should().Be(7946);
        
        eventLog.Should().HaveCount(1);
        eventLog[0].Should().BeOfType<MemberEvent>();
        var memberEvent = (MemberEvent)eventLog[0];
        memberEvent.Type.Should().Be(EventType.MemberJoin);
        memberEvent.Members.Should().HaveCount(1);
        memberEvent.Members[0].Name.Should().Be("node1");
    }
    
    /// <summary>
    /// CRITICAL: HandleNodeJoin resurrects Left members (AUTHORITATIVE).
    /// Per Go: memberlist NotifyJoin is authoritative, always succeeds.
    /// </summary>
    [Fact]
    public void HandleNodeJoin_LeftMember_Resurrects()
    {
        // Arrange
        var memberManager = new MemberManager();
        var eventLog = new List<Event>();
        var clock = new LamportClock();
        
        var handler = new NodeEventHandler(memberManager, eventLog, clock, null, null);
        
        // Add member in Left state
        memberManager.ExecuteUnderLock(accessor =>
        {
            accessor.AddMember(new MemberInfo
            {
                Name = "node1",
                StateMachine = new MemberStateMachine("node1", MemberStatus.Left, 100, null)
            });
        });
        
        var node = new Node
        {
            Name = "node1",
            Addr = IPAddress.Parse("192.168.1.1"),
            Port = 7946,
            Meta = []
        };
        
        // Act
        handler.HandleNodeJoin(node);
        
        // Assert
        var member = memberManager.ExecuteUnderLock(accessor => accessor.GetMember("node1"));
        member!.Status.Should().Be(MemberStatus.Alive, "Left members CAN be resurrected via memberlist join");
        
        eventLog.Should().HaveCount(1, "EventMemberJoin should be emitted");
        eventLog[0].Should().BeOfType<MemberEvent>();
        ((MemberEvent)eventLog[0]).Type.Should().Be(EventType.MemberJoin);
    }
    
    /// <summary>
    /// CRITICAL: HandleNodeJoin resurrects Failed members (AUTHORITATIVE).
    /// Per Go: memberlist NotifyJoin is authoritative, always succeeds.
    /// </summary>
    [Fact]
    public void HandleNodeJoin_FailedMember_Resurrects()
    {
        // Arrange
        var memberManager = new MemberManager();
        var eventLog = new List<Event>();
        var clock = new LamportClock();
        
        var handler = new NodeEventHandler(memberManager, eventLog, clock, null, null);
        
        // Add member in Failed state
        memberManager.ExecuteUnderLock(accessor =>
        {
            accessor.AddMember(new MemberInfo
            {
                Name = "node1",
                StateMachine = new MemberStateMachine("node1", MemberStatus.Failed, 100, null)
            });
        });
        
        var node = new Node
        {
            Name = "node1",
            Addr = IPAddress.Parse("192.168.1.1"),
            Port = 7946,
            Meta = []
        };
        
        // Act
        handler.HandleNodeJoin(node);
        
        // Assert
        var member = memberManager.ExecuteUnderLock(accessor => accessor.GetMember("node1"));
        member!.Status.Should().Be(MemberStatus.Alive, "Failed members CAN be resurrected via memberlist join");
        
        eventLog.Should().HaveCount(1, "EventMemberJoin should be emitted");
    }
    
    /// <summary>
    /// Test: HandleNodeJoin with null node is ignored.
    /// </summary>
    [Fact]
    public void HandleNodeJoin_NullNode_IsIgnored()
    {
        // Arrange
        var memberManager = new MemberManager();
        var eventLog = new List<Event>();
        var clock = new LamportClock();
        
        var handler = new NodeEventHandler(memberManager, eventLog, clock, null, null);
        
        // Act
        handler.HandleNodeJoin(null);
        
        // Assert
        eventLog.Should().BeEmpty();
    }
    
    // ==================== HANDLE NODE LEAVE TESTS ====================
    
    /// <summary>
    /// Test: HandleNodeLeave with Dead state transitions to Failed.
    /// Expected: MemberStatus.Failed, EventMemberFailed emitted.
    /// </summary>
    [Fact]
    public void HandleNodeLeave_DeadState_TransitionsToFailed()
    {
        // Arrange
        var memberManager = new MemberManager();
        var eventLog = new List<Event>();
        var clock = new LamportClock();
        
        var handler = new NodeEventHandler(memberManager, eventLog, clock, null, null);
        
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
        
        var node = new Node
        {
            Name = "node1",
            Addr = IPAddress.Parse("192.168.1.1"),
            Port = 7946,
            State = NodeStateType.Dead,  // Dead = failure
            Meta = []
        };
        
        // Act
        handler.HandleNodeLeave(node);
        
        // Assert
        var member = memberManager.ExecuteUnderLock(accessor => accessor.GetMember("node1"));
        member!.Status.Should().Be(MemberStatus.Failed);
        
        eventLog.Should().HaveCount(1);
        eventLog[0].Should().BeOfType<MemberEvent>();
        var memberEvent = (MemberEvent)eventLog[0];
        memberEvent.Type.Should().Be(EventType.MemberFailed);
        memberEvent.Members[0].Status.Should().Be(MemberStatus.Failed);
    }
    
    /// <summary>
    /// Test: HandleNodeLeave with Left state transitions to Left.
    /// Expected: MemberStatus.Left, EventMemberLeave emitted.
    /// </summary>
    [Fact]
    public void HandleNodeLeave_LeftState_TransitionsToLeft()
    {
        // Arrange
        var memberManager = new MemberManager();
        var eventLog = new List<Event>();
        var clock = new LamportClock();
        
        var handler = new NodeEventHandler(memberManager, eventLog, clock, null, null);
        
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
        
        var node = new Node
        {
            Name = "node1",
            Addr = IPAddress.Parse("192.168.1.1"),
            Port = 7946,
            State = NodeStateType.Left,  // Left = graceful leave
            Meta = []
        };
        
        // Act
        handler.HandleNodeLeave(node);
        
        // Assert
        var member = memberManager.ExecuteUnderLock(accessor => accessor.GetMember("node1"));
        member!.Status.Should().Be(MemberStatus.Left);
        
        eventLog.Should().HaveCount(1);
        eventLog[0].Should().BeOfType<MemberEvent>();
        var memberEvent = (MemberEvent)eventLog[0];
        memberEvent.Type.Should().Be(EventType.MemberLeave);
        memberEvent.Members[0].Status.Should().Be(MemberStatus.Left);
    }
    
    /// <summary>
    /// Test: HandleNodeLeave sets LeaveTime.
    /// Expected: LeaveTime is set to current time.
    /// </summary>
    [Fact]
    public void HandleNodeLeave_SetsLeaveTime()
    {
        // Arrange
        var memberManager = new MemberManager();
        var eventLog = new List<Event>();
        var clock = new LamportClock();
        
        var handler = new NodeEventHandler(memberManager, eventLog, clock, null, null);
        
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
        
        var node = new Node
        {
            Name = "node1",
            State = NodeStateType.Dead,
            Meta = []
        };
        
        var beforeLeave = DateTimeOffset.UtcNow;
        
        // Act
        handler.HandleNodeLeave(node);
        
        // Assert
        var member = memberManager.ExecuteUnderLock(accessor => accessor.GetMember("node1"));
        member!.LeaveTime.Should().BeCloseTo(beforeLeave, TimeSpan.FromSeconds(1));
    }
    
    /// <summary>
    /// Test: HandleNodeLeave with null node is ignored.
    /// </summary>
    [Fact]
    public void HandleNodeLeave_NullNode_IsIgnored()
    {
        // Arrange
        var memberManager = new MemberManager();
        var eventLog = new List<Event>();
        var clock = new LamportClock();
        
        var handler = new NodeEventHandler(memberManager, eventLog, clock, null, null);
        
        // Act
        handler.HandleNodeLeave(null);
        
        // Assert
        eventLog.Should().BeEmpty();
    }
    
    /// <summary>
    /// Test: HandleNodeLeave for unknown member is ignored.
    /// Expected: No errors, no events.
    /// </summary>
    [Fact]
    public void HandleNodeLeave_UnknownMember_IsIgnored()
    {
        // Arrange
        var memberManager = new MemberManager();
        var eventLog = new List<Event>();
        var clock = new LamportClock();
        
        var handler = new NodeEventHandler(memberManager, eventLog, clock, null, null);
        
        var node = new Node
        {
            Name = "unknown-node",
            State = NodeStateType.Dead,
            Meta = []
        };
        
        // Act
        handler.HandleNodeLeave(node);
        
        // Assert
        eventLog.Should().BeEmpty("no event should be emitted for unknown member");
    }
    
    // ==================== EDGE CASES FROM DEEPWIKI ====================
    
    /// <summary>
    /// EDGE CASE: Same node joins multiple times.
    /// Per Go: Status is updated to Alive each time, node info is refreshed.
    /// </summary>
    [Fact]
    public void HandleNodeJoin_SameNodeMultipleTimes_UpdatesEachTime()
    {
        // Arrange
        var memberManager = new MemberManager();
        var eventLog = new List<Event>();
        var clock = new LamportClock();
        
        var handler = new NodeEventHandler(memberManager, eventLog, clock, null, null);
        
        var node = new Node
        {
            Name = "node1",
            Addr = IPAddress.Parse("192.168.1.1"),
            Port = 7946,
            Meta = []
        };
        
        // Act - Join multiple times
        handler.HandleNodeJoin(node);
        handler.HandleNodeJoin(node);
        handler.HandleNodeJoin(node);
        
        // Assert
        var member = memberManager.ExecuteUnderLock(accessor => accessor.GetMember("node1"));
        member!.Status.Should().Be(MemberStatus.Alive);
        
        eventLog.Should().HaveCount(3, "each join should emit an event");
        eventLog.All(e => ((MemberEvent)e).Type == EventType.MemberJoin).Should().BeTrue();
    }
    
    /// <summary>
    /// EDGE CASE: Node leaves then joins again quickly (within same session).
    /// Per Go: Status transitions Failed→Alive or Left→Alive.
    /// </summary>
    [Fact]
    public void HandleNodeLeave_ThenJoin_WorksCorrectly()
    {
        // Arrange
        var memberManager = new MemberManager();
        var eventLog = new List<Event>();
        var clock = new LamportClock();
        
        var handler = new NodeEventHandler(memberManager, eventLog, clock, null, null);
        
        var node = new Node
        {
            Name = "node1",
            Addr = IPAddress.Parse("192.168.1.1"),
            Port = 7946,
            Meta = []
        };
        
        // Act - Join, Leave (fail), then Join again
        handler.HandleNodeJoin(node);
        
        var leaveNode = new Node
        {
            Name = "node1",
            Addr = IPAddress.Parse("192.168.1.1"),
            Port = 7946,
            State = NodeStateType.Dead,
            Meta = []
        };
        handler.HandleNodeLeave(leaveNode);
        
        handler.HandleNodeJoin(node);
        
        // Assert
        var member = memberManager.ExecuteUnderLock(accessor => accessor.GetMember("node1"));
        member!.Status.Should().Be(MemberStatus.Alive, "node should be resurrected after rejoin");
        
        eventLog.Should().HaveCount(3);
        eventLog[0].Should().BeOfType<MemberEvent>().Which.Type.Should().Be(EventType.MemberJoin);
        eventLog[1].Should().BeOfType<MemberEvent>().Which.Type.Should().Be(EventType.MemberFailed);
        eventLog[2].Should().BeOfType<MemberEvent>().Which.Type.Should().Be(EventType.MemberJoin);
    }
    
    /// <summary>
    /// EDGE CASE: Flap detection - Failed node rejoins within FlapTimeout.
    /// Per Go: LeaveTime should be set when node fails, allowing flap detection.
    /// </summary>
    [Fact]
    public void HandleNodeLeave_SetsLeaveTime_ForFlapDetection()
    {
        // Arrange
        var memberManager = new MemberManager();
        var eventLog = new List<Event>();
        var clock = new LamportClock();
        
        var handler = new NodeEventHandler(memberManager, eventLog, clock, null, null);
        
        // First join
        var joinNode = new Node
        {
            Name = "node1",
            Addr = IPAddress.Parse("192.168.1.1"),
            Port = 7946,
            Meta = []
        };
        handler.HandleNodeJoin(joinNode);
        
        var beforeLeave = DateTimeOffset.UtcNow;
        
        // Then fail
        var leaveNode = new Node
        {
            Name = "node1",
            State = NodeStateType.Dead,
            Meta = []
        };
        handler.HandleNodeLeave(leaveNode);
        
        // Assert - LeaveTime should be set
        var member = memberManager.ExecuteUnderLock(accessor => accessor.GetMember("node1"));
        member!.Status.Should().Be(MemberStatus.Failed);
        member.LeaveTime.Should().BeCloseTo(beforeLeave, TimeSpan.FromSeconds(1));
        
        // Rejoin quickly
        handler.HandleNodeJoin(joinNode);
        
        // Assert - After rejoin, still alive
        member = memberManager.ExecuteUnderLock(accessor => accessor.GetMember("node1"));
        member!.Status.Should().Be(MemberStatus.Alive);
    }
    
    /// <summary>
    /// EDGE CASE: HandleNodeJoin updates existing Alive member.
    /// Per Go: Member info is refreshed even if already Alive.
    /// </summary>
    [Fact]
    public void HandleNodeJoin_AliveToAlive_RefreshesInfo()
    {
        // Arrange
        var memberManager = new MemberManager();
        var eventLog = new List<Event>();
        var clock = new LamportClock();
        
        var handler = new NodeEventHandler(memberManager, eventLog, clock, null, null);
        
        // Add member in Alive state
        memberManager.ExecuteUnderLock(accessor =>
        {
            accessor.AddMember(new MemberInfo
            {
                Name = "node1",
                StateMachine = new MemberStateMachine("node1", MemberStatus.Alive, 100, null),
                Member = new Member 
                { 
                    Name = "node1", 
                    Status = MemberStatus.Alive,
                    Port = 7946 
                }
            });
        });
        
        var node = new Node
        {
            Name = "node1",
            Addr = IPAddress.Parse("192.168.1.100"),
            Port = 8946,  // Different port
            Meta = []
        };
        
        // Act
        handler.HandleNodeJoin(node);
        
        // Assert - Member info should be updated
        var member = memberManager.ExecuteUnderLock(accessor => accessor.GetMember("node1"));
        member!.Status.Should().Be(MemberStatus.Alive);
        member.Member!.Port.Should().Be(8946, "port should be updated");
        member.Member.Addr.Should().Be(IPAddress.Parse("192.168.1.100"), "address should be updated");
        
        eventLog.Should().HaveCount(1, "event should still be emitted for info update");
    }
    
    /// <summary>
    /// EDGE CASE: HandleNodeLeave distinguishes between Dead and Left states.
    /// Per Go: NodeStateType.Dead = Failed, NodeStateType.Left = graceful leave.
    /// </summary>
    [Fact]
    public void HandleNodeLeave_DistinguishesBetweenDeadAndLeft()
    {
        // Arrange
        var memberManager = new MemberManager();
        var eventLog = new List<Event>();
        var clock = new LamportClock();
        
        var handler = new NodeEventHandler(memberManager, eventLog, clock, null, null);
        
        // Setup: Add two alive members
        handler.HandleNodeJoin(new Node
        {
            Name = "node1",
            Addr = IPAddress.Parse("192.168.1.1"),
            Port = 7946,
            Meta = []
        });
        
        handler.HandleNodeJoin(new Node
        {
            Name = "node2",
            Addr = IPAddress.Parse("192.168.1.2"),
            Port = 7946,
            Meta = []
        });
        
        // Clear events from joins
        eventLog.Clear();
        
        // Act - node1 fails (Dead), node2 leaves gracefully (Left)
        handler.HandleNodeLeave(new Node
        {
            Name = "node1",
            State = NodeStateType.Dead,
            Meta = []
        });
        
        handler.HandleNodeLeave(new Node
        {
            Name = "node2",
            State = NodeStateType.Left,
            Meta = []
        });
        
        // Assert
        var member1 = memberManager.ExecuteUnderLock(accessor => accessor.GetMember("node1"));
        var member2 = memberManager.ExecuteUnderLock(accessor => accessor.GetMember("node2"));
        
        member1!.Status.Should().Be(MemberStatus.Failed, "Dead state = Failed");
        member2!.Status.Should().Be(MemberStatus.Left, "Left state = graceful leave");
        
        eventLog.Should().HaveCount(2);
        ((MemberEvent)eventLog[0]).Type.Should().Be(EventType.MemberFailed);
        ((MemberEvent)eventLog[1]).Type.Should().Be(EventType.MemberLeave);
    }
}

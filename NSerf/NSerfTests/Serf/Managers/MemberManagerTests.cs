// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Serf;
using NSerf.Serf.Managers;
using Xunit;

namespace NSerfTests.Serf.Managers;

/// <summary>
/// Tests for MemberManager - transaction pattern and query operations.
/// Phase 2: RED phase - writing tests FIRST before implementation.
/// </summary>
public class MemberManagerTests
{
    // ========== Basic Query Operations ==========
    
    [Fact]
    public void GetMembers_ReturnsAllMembers()
    {
        // Arrange
        var manager = CreateTestManager();
        
        manager.ExecuteUnderLock(accessor =>
        {
            accessor.AddMember(CreateMemberInfo("node1", MemberStatus.Alive));
            accessor.AddMember(CreateMemberInfo("node2", MemberStatus.Alive));
            accessor.AddMember(CreateMemberInfo("node3", MemberStatus.Leaving));
        });
        
        // Act
        var members = manager.ExecuteUnderLock(accessor => 
            accessor.GetAllMembers());
        
        // Assert
        Assert.Equal(3, members.Count);
        Assert.Contains(members, m => m.Name == "node1");
        Assert.Contains(members, m => m.Name == "node2");
        Assert.Contains(members, m => m.Name == "node3");
    }
    
    [Fact]
    public void GetMember_ExistingMember_ReturnsCorrectMember()
    {
        // Arrange
        var manager = CreateTestManager();
        
        manager.ExecuteUnderLock(accessor =>
        {
            accessor.AddMember(CreateMemberInfo("node1", MemberStatus.Alive));
        });
        
        // Act
        var member = manager.ExecuteUnderLock(accessor => 
            accessor.GetMember("node1"));
        
        // Assert
        Assert.NotNull(member);
        Assert.Equal("node1", member.Name);
        Assert.Equal(MemberStatus.Alive, member.Status);
    }
    
    [Fact]
    public void GetMember_NonExistentMember_ReturnsNull()
    {
        // Arrange
        var manager = CreateTestManager();
        
        // Act
        var member = manager.ExecuteUnderLock(accessor => 
            accessor.GetMember("nonexistent"));
        
        // Assert
        Assert.Null(member);
    }
    
    [Fact]
    public void GetMemberCount_ReturnsCorrectCount()
    {
        // Arrange
        var manager = CreateTestManager();
        
        manager.ExecuteUnderLock(accessor =>
        {
            accessor.AddMember(CreateMemberInfo("node1", MemberStatus.Alive));
            accessor.AddMember(CreateMemberInfo("node2", MemberStatus.Alive));
        });
        
        // Act
        var count = manager.ExecuteUnderLock(accessor => 
            accessor.GetMemberCount());
        
        // Assert
        Assert.Equal(2, count);
    }
    
    // ========== Transaction Pattern Tests ==========
    
    [Fact]
    public void ExecuteUnderLock_ProvidesAtomicAccess()
    {
        // Arrange
        var manager = CreateTestManager();
        
        // Act - Entire operation is atomic
        var result = manager.ExecuteUnderLock(accessor =>
        {
            var member = accessor.GetMember("node1");
            if (member == null)
            {
                accessor.AddMember(CreateMemberInfo("node1", MemberStatus.Alive));
                return true;
            }
            return false;
        });
        
        // Assert
        Assert.True(result);
        var added = manager.ExecuteUnderLock(accessor => accessor.GetMember("node1"));
        Assert.NotNull(added);
    }
    
    [Fact]
    public void ExecuteUnderLock_SupportsComplexTransactions()
    {
        // Arrange
        var manager = CreateTestManager();
        
        manager.ExecuteUnderLock(accessor =>
        {
            accessor.AddMember(CreateMemberInfo("node1", MemberStatus.Alive));
        });
        
        // Act - Complex transaction: check status, update, verify
        var result = manager.ExecuteUnderLock(accessor =>
        {
            var member = accessor.GetMember("node1");
            if (member?.Status == MemberStatus.Alive)
            {
                accessor.UpdateMember("node1", m => m.StateMachine.TryTransitionOnLeaveIntent(200));
                var updated = accessor.GetMember("node1");
                return updated?.Status == MemberStatus.Leaving;
            }
            return false;
        });
        
        // Assert
        Assert.True(result);
    }
    
    // ========== Member Manipulation Tests ==========
    
    [Fact]
    public void AddMember_NewMember_Succeeds()
    {
        // Arrange
        var manager = CreateTestManager();
        
        // Act
        manager.ExecuteUnderLock(accessor =>
        {
            accessor.AddMember(CreateMemberInfo("node1", MemberStatus.Alive));
        });
        
        // Assert
        var member = manager.ExecuteUnderLock(accessor => accessor.GetMember("node1"));
        Assert.NotNull(member);
        Assert.Equal("node1", member.Name);
    }
    
    [Fact]
    public void UpdateMember_ExistingMember_Succeeds()
    {
        // Arrange
        var manager = CreateTestManager();
        
        manager.ExecuteUnderLock(accessor =>
        {
            accessor.AddMember(CreateMemberInfo("node1", MemberStatus.Alive));
        });
        
        // Act
        manager.ExecuteUnderLock(accessor =>
        {
            accessor.UpdateMember("node1", m => m.StateMachine.TryTransitionOnLeaveIntent(200));
        });
        
        // Assert
        var member = manager.ExecuteUnderLock(accessor => accessor.GetMember("node1"));
        Assert.NotNull(member);
        Assert.Equal(MemberStatus.Leaving, member.Status);
        Assert.Equal(new LamportTime(200), member.StatusLTime); // LTime updated by transition
    }
    
    [Fact]
    public void RemoveMember_ExistingMember_Succeeds()
    {
        // Arrange
        var manager = CreateTestManager();
        
        manager.ExecuteUnderLock(accessor =>
        {
            accessor.AddMember(CreateMemberInfo("node1", MemberStatus.Alive));
        });
        
        // Act
        var removed = manager.ExecuteUnderLock(accessor => 
            accessor.RemoveMember("node1"));
        
        // Assert
        Assert.True(removed);
        var member = manager.ExecuteUnderLock(accessor => accessor.GetMember("node1"));
        Assert.Null(member);
    }
    
    [Fact]
    public void RemoveMember_NonExistentMember_ReturnsFalse()
    {
        // Arrange
        var manager = CreateTestManager();
        
        // Act
        var removed = manager.ExecuteUnderLock(accessor => 
            accessor.RemoveMember("nonexistent"));
        
        // Assert
        Assert.False(removed);
    }
    
    // ========== Failed/Left Member Tracking Tests ==========
    
    [Fact]
    public void GetFailedMembers_ReturnsOnlyFailedMembers()
    {
        // Arrange
        var manager = CreateTestManager();
        
        manager.ExecuteUnderLock(accessor =>
        {
            accessor.AddMember(CreateMemberInfo("node1", MemberStatus.Alive));
            accessor.AddMember(CreateMemberInfo("node2", MemberStatus.Failed));
            accessor.AddMember(CreateMemberInfo("node3", MemberStatus.Failed));
            accessor.AddMember(CreateMemberInfo("node4", MemberStatus.Left));
        });
        
        // Act
        var failed = manager.ExecuteUnderLock(accessor => 
            accessor.GetFailedMembers());
        
        // Assert
        Assert.Equal(2, failed.Count);
        Assert.All(failed, m => Assert.Equal(MemberStatus.Failed, m.Status));
    }
    
    [Fact]
    public void GetLeftMembers_ReturnsOnlyLeftMembers()
    {
        // Arrange
        var manager = CreateTestManager();
        
        manager.ExecuteUnderLock(accessor =>
        {
            accessor.AddMember(CreateMemberInfo("node1", MemberStatus.Alive));
            accessor.AddMember(CreateMemberInfo("node2", MemberStatus.Left));
            accessor.AddMember(CreateMemberInfo("node3", MemberStatus.Left));
            accessor.AddMember(CreateMemberInfo("node4", MemberStatus.Failed));
        });
        
        // Act
        var left = manager.ExecuteUnderLock(accessor => 
            accessor.GetLeftMembers());
        
        // Assert
        Assert.Equal(2, left.Count);
        Assert.All(left, m => Assert.Equal(MemberStatus.Left, m.Status));
    }
    
    // ========== Filter Operations Tests ==========
    
    [Fact]
    public void GetMembersByStatus_FiltersCorrectly()
    {
        // Arrange
        var manager = CreateTestManager();
        
        manager.ExecuteUnderLock(accessor =>
        {
            accessor.AddMember(CreateMemberInfo("node1", MemberStatus.Alive));
            accessor.AddMember(CreateMemberInfo("node2", MemberStatus.Alive));
            accessor.AddMember(CreateMemberInfo("node3", MemberStatus.Leaving));
        });
        
        // Act
        var alive = manager.ExecuteUnderLock(accessor => 
            accessor.GetMembersByStatus(MemberStatus.Alive));
        
        // Assert
        Assert.Equal(2, alive.Count);
        Assert.All(alive, m => Assert.Equal(MemberStatus.Alive, m.Status));
    }
    
    // ========== Thread Safety Tests ==========
    
    [Fact]
    public async Task ExecuteUnderLock_IsThreadSafe()
    {
        // Arrange
        var manager = CreateTestManager();
        var tasks = new List<Task>();
        
        // Act - Multiple concurrent operations
        for (int i = 0; i < 10; i++)
        {
            int nodeNum = i;
            tasks.Add(Task.Run(() =>
            {
                manager.ExecuteUnderLock(accessor =>
                {
                    accessor.AddMember(CreateMemberInfo($"node{nodeNum}", MemberStatus.Alive));
                });
            }));
        }
        
        await Task.WhenAll(tasks);
        
        // Assert
        var count = manager.ExecuteUnderLock(accessor => accessor.GetMemberCount());
        Assert.Equal(10, count);
    }
    
    // ========== Helper Methods ==========
    
    private MemberManager CreateTestManager()
    {
        return new MemberManager();
    }
    
    private MemberInfo CreateMemberInfo(string name, MemberStatus status)
    {
        return new MemberInfo
        {
            Name = name,
            StateMachine = new NSerf.Serf.StateMachine.MemberStateMachine(
                name,
                status,
                new LamportTime(100),
                null),
            Member = new Member { Name = name, Status = status }
        };
    }
}

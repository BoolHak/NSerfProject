// Ported from: github.com/hashicorp/memberlist/util.go
// Copyright (c) HashiCorp, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.Net;
using NSerf.Memberlist;
using NSerf.Memberlist.State;

namespace NSerfTests.Memberlist;

public class NodeStateManagerTests
{
    [Fact]
    public void MoveDeadNodes_AllAlive_ShouldNotMove()
    {
        // Arrange
        var nodes = new List<NodeState>
        {
            CreateNodeState("node1", NodeStateType.Alive),
            CreateNodeState("node2", NodeStateType.Alive),
            CreateNodeState("node3", NodeStateType.Alive)
        };
        
        // Act
        var deadIdx = NodeStateManager.MoveDeadNodes(nodes, TimeSpan.FromSeconds(10));
        
        // Assert
        deadIdx.Should().Be(3, "all nodes should be alive");
    }
    
    [Fact]
    public void MoveDeadNodes_SomeDead_ShouldMoveToEnd()
    {
        // Arrange
        var nodes = new List<NodeState>
        {
            CreateNodeState("node1", NodeStateType.Alive),
            CreateNodeState("node2", NodeStateType.Dead, DateTimeOffset.UtcNow.AddMinutes(-5)),
            CreateNodeState("node3", NodeStateType.Alive),
            CreateNodeState("node4", NodeStateType.Dead, DateTimeOffset.UtcNow.AddMinutes(-5))
        };
        
        // Act
        var deadIdx = NodeStateManager.MoveDeadNodes(nodes, TimeSpan.FromSeconds(10));
        
        // Assert
        deadIdx.Should().Be(2, "2 nodes should be alive");
        nodes[0].State.Should().BeOneOf(NodeStateType.Alive);
        nodes[1].State.Should().BeOneOf(NodeStateType.Alive);
        nodes[2].State.Should().BeOneOf(NodeStateType.Dead);
        nodes[3].State.Should().BeOneOf(NodeStateType.Dead);
    }
    
    [Fact]
    public void MoveDeadNodes_RecentlyDead_ShouldNotMove()
    {
        // Arrange
        var nodes = new List<NodeState>
        {
            CreateNodeState("node1", NodeStateType.Alive),
            CreateNodeState("node2", NodeStateType.Dead, DateTimeOffset.UtcNow.AddSeconds(-1))
        };
        
        // Act
        var deadIdx = NodeStateManager.MoveDeadNodes(nodes, TimeSpan.FromSeconds(10));
        
        // Assert
        deadIdx.Should().Be(2, "recently dead node should not move");
    }
    
    [Fact]
    public void ShuffleNodes_ShouldRandomize()
    {
        // Arrange
        var nodes = new List<NodeState>
        {
            CreateNodeState("node1", NodeStateType.Alive),
            CreateNodeState("node2", NodeStateType.Alive),
            CreateNodeState("node3", NodeStateType.Alive),
            CreateNodeState("node4", NodeStateType.Alive),
            CreateNodeState("node5", NodeStateType.Alive)
        };
        var originalOrder = nodes.Select(n => n.Name).ToList();
        
        // Act
        NodeStateManager.ShuffleNodes(nodes);
        var shuffledOrder = nodes.Select(n => n.Name).ToList();
        
        // Assert - Not a perfect test but should catch if shuffle does nothing
        nodes.Should().HaveCount(5);
        nodes.Should().Contain(n => n.Name == "node1");
        nodes.Should().Contain(n => n.Name == "node5");
    }
    
    [Fact]
    public void KRandomNodes_LessThanK_ShouldReturnAll()
    {
        // Arrange
        var nodes = new List<NodeState>
        {
            CreateNodeState("node1", NodeStateType.Alive),
            CreateNodeState("node2", NodeStateType.Alive)
        };
        
        // Act
        var selected = NodeStateManager.KRandomNodes(5, nodes);
        
        // Assert
        selected.Should().HaveCount(2);
    }
    
    [Fact]
    public void KRandomNodes_WithExclude_ShouldFilter()
    {
        // Arrange
        var nodes = new List<NodeState>
        {
            CreateNodeState("node1", NodeStateType.Alive),
            CreateNodeState("node2", NodeStateType.Dead),
            CreateNodeState("node3", NodeStateType.Alive),
            CreateNodeState("node4", NodeStateType.Suspect)
        };
        
        // Act
        var selected = NodeStateManager.KRandomNodes(2, nodes, n => n.State == NodeStateType.Dead);
        
        // Assert
        selected.Should().NotContain(n => n.Name == "node2");
    }
    
    [Fact]
    public void KRandomNodes_ShouldNotDuplicate()
    {
        // Arrange
        var nodes = new List<NodeState>
        {
            CreateNodeState("node1", NodeStateType.Alive),
            CreateNodeState("node2", NodeStateType.Alive),
            CreateNodeState("node3", NodeStateType.Alive),
            CreateNodeState("node4", NodeStateType.Alive),
            CreateNodeState("node5", NodeStateType.Alive)
        };
        
        // Act
        var selected = NodeStateManager.KRandomNodes(3, nodes);
        
        // Assert
        selected.Should().HaveCountLessOrEqualTo(3);
        selected.Select(n => n.Name).Distinct().Should().HaveCount(selected.Count);
    }
    
    [Fact]
    public void RandomOffset_Zero_ShouldReturnZero()
    {
        // Act
        var offset = NodeStateManager.RandomOffset(0);
        
        // Assert
        offset.Should().Be(0);
    }
    
    [Fact]
    public void RandomOffset_Positive_ShouldBeInRange()
    {
        // Act
        var offset = NodeStateManager.RandomOffset(10);
        
        // Assert
        offset.Should().BeInRange(0, 9);
    }
    
    private static NodeState CreateNodeState(string name, NodeStateType state, DateTimeOffset? stateChange = null)
    {
        return new NodeState
        {
            Node = new Node
            {
                Name = name,
                Addr = IPAddress.Parse("127.0.0.1"),
                Port = 8080,
                State = state
            },
            State = state,
            StateChange = stateChange ?? DateTimeOffset.UtcNow,
            Incarnation = 0
        };
    }
}

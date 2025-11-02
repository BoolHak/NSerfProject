// Ported from: github.com/hashicorp/memberlist/util_test.go
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Memberlist.Common;
using NSerf.Memberlist.State;

namespace NSerfTests.Memberlist.Common;

public class CollectionUtilsTests
{
    [Fact]
    public void ShuffleNodes_ShouldRandomizeOrder()
    {
        // Arrange
        var original = CreateTestNodeStates(8);
        var nodes = original.ToArray(); // Copy

        // Act
        CollectionUtils.ShuffleNodes(nodes);

        // Assert - should be different order (high probability)
        nodes.Should().NotBeEquivalentTo(original, options => options.WithStrictOrdering());
    }

    [Fact]
    public void ShuffleNodes_ShouldPreserveAllNodes()
    {
        // Arrange
        var nodes = CreateTestNodeStates(8);
        var originalNames = nodes.Select(n => n.Node.Name).ToHashSet();

        // Act
        CollectionUtils.ShuffleNodes(nodes);

        // Assert - should have same nodes, just different order
        var shuffledNames = nodes.Select(n => n.Node.Name).ToHashSet();
        shuffledNames.Should().BeEquivalentTo(originalNames);
    }

    [Fact]
    public void MoveDeadNodes_ShouldMoveOldDeadNodesToEnd()
    {
        // Arrange
        var nodes = new[]
        {
            CreateNodeState("node0", NodeStateType.Dead, TimeSpan.FromSeconds(-20)),
            CreateNodeState("node1", NodeStateType.Alive, TimeSpan.FromSeconds(-20)),
            CreateNodeState("node2", NodeStateType.Dead, TimeSpan.FromSeconds(-10)),  // Recent - should stay
            CreateNodeState("node3", NodeStateType.Left, TimeSpan.FromSeconds(-10)),   // Recent - should stay
            CreateNodeState("node4", NodeStateType.Left, TimeSpan.FromSeconds(-20)),
            CreateNodeState("node5", NodeStateType.Alive, TimeSpan.FromSeconds(-20)),
            CreateNodeState("node6", NodeStateType.Dead, TimeSpan.FromSeconds(-20)),
            CreateNodeState("node7", NodeStateType.Alive, TimeSpan.FromSeconds(-20)),
            CreateNodeState("node8", NodeStateType.Left, TimeSpan.FromSeconds(-20))
        };

        // Act
        var firstDeadIndex = CollectionUtils.MoveDeadNodes(nodes, TimeSpan.FromSeconds(15));

        // Assert
        firstDeadIndex.Should().Be(5, "should have 5 alive/recent nodes");

        // First 5 should be alive or recent dead/left
        for (int i = 0; i < firstDeadIndex; i++)
        {
            if (i == 2)
            {
                nodes[i].State.Should().Be(NodeStateType.Dead, $"node at {i} should be recent dead");
            }
            else if (i == 3)
            {
                nodes[i].State.Should().Be(NodeStateType.Left, $"node at {i} should be recent left");
            }
            else
            {
                nodes[i].State.Should().Be(NodeStateType.Alive, $"node at {i} should be alive");
            }
        }

        // After firstDeadIndex should be old dead/left
        for (int i = firstDeadIndex; i < nodes.Length; i++)
        {
            nodes[i].DeadOrLeft().Should().BeTrue($"node at {i} should be dead or left");
        }
    }

    [Fact]
    public void KRandomNodes_ShouldReturnUpToKNodes()
    {
        // Arrange
        var nodes = Enumerable.Range(0, 90)
            .Select(i => CreateNodeState($"test{i}",
                i % 3 == 0 ? NodeStateType.Alive :
                i % 3 == 1 ? NodeStateType.Suspect :
                NodeStateType.Dead,
                TimeSpan.Zero))
            .ToArray();

        // Act
        var result1 = CollectionUtils.KRandomNodes(3, nodes, FilterFunc);
        var result2 = CollectionUtils.KRandomNodes(3, nodes, FilterFunc);
        var result3 = CollectionUtils.KRandomNodes(3, nodes, FilterFunc);

        // Assert
        result1.Should().HaveCount(3);
        result2.Should().HaveCount(3);
        result3.Should().HaveCount(3);

        // Should be different (high probability)
        result1.Should().NotBeEquivalentTo(result2, options => options.WithStrictOrdering());
        result1.Should().NotBeEquivalentTo(result3, options => options.WithStrictOrdering());

        // All should be alive and not test0
        foreach (var node in result1.Concat(result2).Concat(result3))
        {
            node.Name.Should().NotBe("test0");
            node.State.Should().Be(NodeStateType.Alive);
        }
    }

    private static bool FilterFunc(NodeState n) =>
                n.Node.Name == "test0" || n.State != NodeStateType.Alive;

    private static NodeState[] CreateTestNodeStates(int count)
    {
        return [.. Enumerable.Range(0, count)
            .Select(i => CreateNodeState($"node{i}",
                i % 2 == 0 ? NodeStateType.Dead : NodeStateType.Alive,
                TimeSpan.Zero))];
    }

    private static NodeState CreateNodeState(string name, NodeStateType state, TimeSpan timeAgo)
    {
        return new NodeState
        {
            Node = new Node { Name = name },
            State = state,
            StateChange = DateTimeOffset.UtcNow.Add(timeAgo)
        };
    }
}

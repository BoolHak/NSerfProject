// Ported from: github.com/hashicorp/memberlist/queue.go
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Memberlist;
using NSerf.Memberlist.Common;

namespace NSerfTests.Memberlist;

public class TransmitLimitedQueueTests
{
    private class TestBroadcast : IBroadcast
    {
        private readonly string _name;
        private readonly byte[] _message;
        public bool FinishedCalled { get; private set; }
        
        public TestBroadcast(string name, string message)
        {
            _name = name;
            _message = System.Text.Encoding.UTF8.GetBytes(message);
        }
        
        public bool Invalidates(IBroadcast other)
        {
            if (other is TestBroadcast tb)
            {
                return _name == tb._name;
            }
            return false;
        }
        
        public byte[] Message() => _message;
        
        public void Finished()
        {
            FinishedCalled = true;
        }
    }
    
    [Fact]
    public void QueueBroadcast_ShouldAddMessage()
    {
        // Arrange
        var queue = new TransmitLimitedQueue
        {
            RetransmitMult = 3,
            NumNodes = () => 10
        };
        var broadcast = new TestBroadcast("node1", "test message");
        
        // Act
        queue.QueueBroadcast(broadcast);
        
        // Assert
        queue.NumQueued().Should().Be(1);
    }
    
    [Fact]
    public void QueueBroadcast_Invalidation_ShouldReplaceOldMessage()
    {
        // Arrange
        var queue = new TransmitLimitedQueue
        {
            RetransmitMult = 3,
            NumNodes = () => 10
        };
        var broadcast1 = new TestBroadcast("node1", "old message");
        var broadcast2 = new TestBroadcast("node1", "new message");
        
        // Act
        queue.QueueBroadcast(broadcast1);
        queue.QueueBroadcast(broadcast2);
        
        // Assert
        queue.NumQueued().Should().Be(1, "new message should invalidate old one");
        broadcast1.FinishedCalled.Should().BeTrue("old broadcast should be finished");
    }
    
    [Fact]
    public void GetBroadcasts_ShouldReturnMessagesWithinLimit()
    {
        // Arrange
        var queue = new TransmitLimitedQueue
        {
            RetransmitMult = 3,
            NumNodes = () => 10
        };
        queue.QueueBroadcast(new TestBroadcast("node1", "msg1"));
        queue.QueueBroadcast(new TestBroadcast("node2", "msg2"));
        queue.QueueBroadcast(new TestBroadcast("node3", "msg3"));
        
        // Act
        var broadcasts = queue.GetBroadcasts(overhead: 0, limit: 1000);
        
        // Assert
        broadcasts.Should().HaveCount(3);
    }
    
    [Fact]
    public void GetBroadcasts_WithByteLimit_ShouldRespectLimit()
    {
        // Arrange
        var queue = new TransmitLimitedQueue
        {
            RetransmitMult = 3,
            NumNodes = () => 10
        };
        queue.QueueBroadcast(new TestBroadcast("node1", "message1"));
        queue.QueueBroadcast(new TestBroadcast("node2", "message2"));
        
        // Act - Only enough space for one message
        var broadcasts = queue.GetBroadcasts(overhead: 0, limit: 10);
        
        // Assert
        broadcasts.Should().HaveCountLessOrEqualTo(2);
        broadcasts.Sum(b => b.Length).Should().BeLessOrEqualTo(10);
    }
    
    [Fact]
    public void GetBroadcasts_ShouldIncrementTransmitCount()
    {
        // Arrange
        var queue = new TransmitLimitedQueue
        {
            RetransmitMult = 3,
            NumNodes = () => 10
        };
        var broadcast = new TestBroadcast("node1", "test");
        queue.QueueBroadcast(broadcast);
        
        // Calculate transmit limit
        int transmitLimit = MemberlistMath.RetransmitLimit(3, 10);
        
        // Act - Get broadcasts multiple times
        for (int i = 0; i < transmitLimit; i++)
        {
            var broadcasts = queue.GetBroadcasts(overhead: 0, limit: 1000);
            broadcasts.Should().NotBeEmpty($"iteration {i}");
        }
        
        // After transmit limit, should be removed
        var finalBroadcasts = queue.GetBroadcasts(overhead: 0, limit: 1000);
        
        // Assert
        finalBroadcasts.Should().BeEmpty("message should be removed after transmit limit");
        broadcast.FinishedCalled.Should().BeTrue("broadcast should be finished");
    }
    
    [Fact]
    public void NumQueued_ShouldReturnCorrectCount()
    {
        // Arrange
        var queue = new TransmitLimitedQueue
        {
            RetransmitMult = 3,
            NumNodes = () => 10
        };
        
        // Act & Assert
        queue.NumQueued().Should().Be(0);
        
        queue.QueueBroadcast(new TestBroadcast("node1", "msg1"));
        queue.NumQueued().Should().Be(1);
        
        queue.QueueBroadcast(new TestBroadcast("node2", "msg2"));
        queue.NumQueued().Should().Be(2);
    }
    
    [Fact]
    public void Reset_ShouldClearAllMessages()
    {
        // Arrange
        var queue = new TransmitLimitedQueue
        {
            RetransmitMult = 3,
            NumNodes = () => 10
        };
        var broadcast1 = new TestBroadcast("node1", "msg1");
        var broadcast2 = new TestBroadcast("node2", "msg2");
        queue.QueueBroadcast(broadcast1);
        queue.QueueBroadcast(broadcast2);
        
        // Act
        queue.Reset();
        
        // Assert
        queue.NumQueued().Should().Be(0);
        broadcast1.FinishedCalled.Should().BeTrue();
        broadcast2.FinishedCalled.Should().BeTrue();
    }
    
    [Fact]
    public void Prune_ShouldRetainOnlyNewestMessages()
    {
        // Arrange
        var queue = new TransmitLimitedQueue
        {
            RetransmitMult = 3,
            NumNodes = () => 10
        };
        queue.QueueBroadcast(new TestBroadcast("node1", "msg1"));
        queue.QueueBroadcast(new TestBroadcast("node2", "msg2"));
        queue.QueueBroadcast(new TestBroadcast("node3", "msg3"));
        
        // Act
        queue.Prune(maxRetain: 2);
        
        // Assert
        queue.NumQueued().Should().Be(2);
    }
    
    [Fact]
    public void GetBroadcasts_PrioritizesLowerTransmitCounts()
    {
        // Arrange
        var queue = new TransmitLimitedQueue
        {
            RetransmitMult = 3,
            NumNodes = () => 10
        };
        var broadcast1 = new TestBroadcast("node1", "message1");
        var broadcast2 = new TestBroadcast("node2", "message2");
        
        queue.QueueBroadcast(broadcast1);
        // Get once to increment transmit count
        queue.GetBroadcasts(overhead: 0, limit: 1000);
        
        // Now add new broadcast
        queue.QueueBroadcast(broadcast2);
        
        // Act - Get broadcasts
        var broadcasts = queue.GetBroadcasts(overhead: 0, limit: 1000);
        
        // Assert - Should get broadcast2 first (lower transmit count)
        broadcasts.Should().HaveCount(2);
        // The first one should be the newer message (lower transmit count)
        System.Text.Encoding.UTF8.GetString(broadcasts[0]).Should().Be("message2");
    }
}

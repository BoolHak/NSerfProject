// Ported from: github.com/hashicorp/memberlist/queue.go
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Memberlist;
using NSerf.Memberlist.Broadcast;

namespace NSerfTests.Memberlist;

public class BroadcastTests
{
    private class TestBroadcast : IBroadcast
    {
        private readonly string _name;
        private readonly byte[] _message;
        public bool FinishedCalled { get; private set; }
        
        public TestBroadcast(string name, byte[] message)
        {
            _name = name;
            _message = message;
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
    public void Broadcast_Invalidates_SameName_ShouldReturnTrue()
    {
        // Arrange
        var broadcast1 = new TestBroadcast("node1", "message1"u8.ToArray());
        var broadcast2 = new TestBroadcast("node1", "message2"u8.ToArray());
        
        // Act
        var result = broadcast1.Invalidates(broadcast2);
        
        // Assert
        result.Should().BeTrue("broadcasts about the same node should invalidate each other");
    }
    
    [Fact]
    public void Broadcast_Invalidates_DifferentName_ShouldReturnFalse()
    {
        // Arrange
        var broadcast1 = new TestBroadcast("node1", "message1"u8.ToArray());
        var broadcast2 = new TestBroadcast("node2", "message2"u8.ToArray());
        
        // Act
        var result = broadcast1.Invalidates(broadcast2);
        
        // Assert
        result.Should().BeFalse("broadcasts about different nodes should not invalidate each other");
    }
    
    [Fact]
    public void Broadcast_Message_ShouldReturnMessageBytes()
    {
        // Arrange
        var messageBytes = "test message"u8.ToArray();
        var broadcast = new TestBroadcast("node1", messageBytes);
        
        // Act
        var result = broadcast.Message();
        
        // Assert
        result.Should().BeEquivalentTo(messageBytes);
    }
    
    [Fact]
    public void Broadcast_Finished_ShouldBeCalled()
    {
        // Arrange
        var broadcast = new TestBroadcast("node1", "message"u8.ToArray());
        
        // Act
        broadcast.Finished();
        
        // Assert
        broadcast.FinishedCalled.Should().BeTrue();
    }
}

public class NamedBroadcastTests
{
    private class TestNamedBroadcast : INamedBroadcast
    {
        private readonly string _name;
        private readonly byte[] _message;
        public bool FinishedCalled { get; private set; }
        
        public TestNamedBroadcast(string name, byte[] message)
        {
            _name = name;
            _message = message;
        }
        
        public string Name() => _name;
        
        public bool Invalidates(IBroadcast other)
        {
            if (other is INamedBroadcast nb)
            {
                return _name == nb.Name();
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
    public void NamedBroadcast_Name_ShouldReturnName()
    {
        // Arrange
        var broadcast = new TestNamedBroadcast("test-node", "message"u8.ToArray());
        
        // Act
        var name = broadcast.Name();
        
        // Assert
        name.Should().Be("test-node");
    }
    
    [Fact]
    public void NamedBroadcast_Invalidates_SameName_ShouldReturnTrue()
    {
        // Arrange
        var broadcast1 = new TestNamedBroadcast("node1", "message1"u8.ToArray());
        var broadcast2 = new TestNamedBroadcast("node1", "message2"u8.ToArray());
        
        // Act
        var result = broadcast1.Invalidates(broadcast2);
        
        // Assert
        result.Should().BeTrue("named broadcasts with same name should invalidate");
    }
}

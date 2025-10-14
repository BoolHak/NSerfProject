// Ported from: github.com/hashicorp/memberlist/net.go
// Copyright (c) HashiCorp, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Memberlist.Messages;
using NSerf.Memberlist.State;

namespace NSerfTests.Memberlist.Messages;

public class ProtocolMessagesTests
{
    [Fact]
    public void PingMessage_ShouldStoreRequiredFields()
    {
        // Arrange & Act
        var ping = new PingMessage
        {
            SeqNo = 12345,
            Node = "test-node",
            SourceAddr = new byte[] { 192, 168, 1, 100 },
            SourcePort = 7946,
            SourceNode = "source-node"
        };
        
        // Assert
        ping.SeqNo.Should().Be(12345);
        ping.Node.Should().Be("test-node");
        ping.SourceAddr.Should().BeEquivalentTo(new byte[] { 192, 168, 1, 100 });
        ping.SourcePort.Should().Be(7946);
        ping.SourceNode.Should().Be("source-node");
    }
    
    [Fact]
    public void IndirectPingMessage_ShouldStoreRequiredFields()
    {
        // Arrange & Act
        var indirectPing = new IndirectPingMessage
        {
            SeqNo = 100,
            Target = new byte[] { 10, 0, 0, 1 },
            Port = 8080,
            Node = "target-node",
            Nack = true
        };
        
        // Assert
        indirectPing.SeqNo.Should().Be(100);
        indirectPing.Target.Should().BeEquivalentTo(new byte[] { 10, 0, 0, 1 });
        indirectPing.Port.Should().Be(8080);
        indirectPing.Node.Should().Be("target-node");
        indirectPing.Nack.Should().BeTrue();
    }
    
    [Fact]
    public void AckRespMessage_ShouldStorePayload()
    {
        // Arrange & Act
        var ack = new AckRespMessage
        {
            SeqNo = 999,
            Payload = new byte[] { 1, 2, 3, 4 }
        };
        
        // Assert
        ack.SeqNo.Should().Be(999);
        ack.Payload.Should().BeEquivalentTo(new byte[] { 1, 2, 3, 4 });
    }
    
    [Fact]
    public void SuspectMessage_ShouldIncludeSourceInfo()
    {
        // Arrange & Act
        var suspect = new SuspectMessage
        {
            Incarnation = 5,
            Node = "suspect-node",
            From = "accuser-node"
        };
        
        // Assert
        suspect.Incarnation.Should().Be(5);
        suspect.Node.Should().Be("suspect-node");
        suspect.From.Should().Be("accuser-node");
    }
    
    [Fact]
    public void AliveMessage_ShouldStoreFullNodeInfo()
    {
        // Arrange & Act
        var alive = new AliveMessage
        {
            Incarnation = 10,
            Node = "alive-node",
            Addr = new byte[] { 192, 168, 1, 50 },
            Port = 7946,
            Meta = new byte[] { 0x01, 0x02 },
            Vsn = new byte[] { 1, 5, 5, 2, 5, 5 }
        };
        
        // Assert
        alive.Incarnation.Should().Be(10);
        alive.Node.Should().Be("alive-node");
        alive.Port.Should().Be(7946);
        alive.Vsn.Should().HaveCount(6);
    }
    
    [Fact]
    public void DeadMessage_ShouldIncludeSourceInfo()
    {
        // Arrange & Act
        var dead = new DeadMessage
        {
            Incarnation = 15,
            Node = "dead-node",
            From = "reporter-node"
        };
        
        // Assert
        dead.Incarnation.Should().Be(15);
        dead.Node.Should().Be("dead-node");
        dead.From.Should().Be("reporter-node");
    }
    
    [Fact]
    public void PushPullHeader_ShouldStoreStateInfo()
    {
        // Arrange & Act
        var header = new PushPullHeader
        {
            Nodes = 100,
            UserStateLen = 1024,
            Join = true
        };
        
        // Assert
        header.Nodes.Should().Be(100);
        header.UserStateLen.Should().Be(1024);
        header.Join.Should().BeTrue();
    }
    
    [Fact]
    public void PushNodeState_ShouldMatchNodeStructure()
    {
        // Arrange & Act
        var nodeState = new PushNodeState
        {
            Name = "node-1",
            Addr = new byte[] { 10, 0, 0, 1 },
            Port = 7946,
            Meta = new byte[] { 0xFF },
            Incarnation = 20,
            State = NodeStateType.Alive,
            Vsn = new byte[] { 1, 5, 5, 2, 5, 5 }
        };
        
        // Assert
        nodeState.Name.Should().Be("node-1");
        nodeState.Incarnation.Should().Be(20);
        nodeState.State.Should().Be(NodeStateType.Alive);
    }
}

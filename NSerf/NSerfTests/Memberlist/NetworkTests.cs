// Ported from: github.com/hashicorp/memberlist/net_test.go
// Copyright (c) HashiCorp, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.Net;
using FluentAssertions;
using NSerf.Memberlist;
using NSerf.Memberlist.Configuration;
using NSerfTests.Memberlist.Transport;
using Xunit;
using MessageEncoder = NSerf.Memberlist.Messages.MessageEncoder;
using MessageType = NSerf.Memberlist.Messages.MessageType;
using PingMessage = NSerf.Memberlist.Messages.PingMessage;
using CompoundMessage = NSerf.Memberlist.Messages.CompoundMessage;

namespace NSerfTests.Memberlist;

public class NetworkTests
{
    private MockNetwork CreateMockNetwork()
    {
        return new MockNetwork();
    }

    private MemberlistConfig CreateTestConfig(string name)
    {
        var config = MemberlistConfig.DefaultLANConfig();
        config.Name = name;
        config.BindPort = 0;
        config.Logger = null;
        return config;
    }

    [Fact]
    public void MessageEncoder_CompoundMessage_CreatesCorrectly()
    {
        var ping = new PingMessage
        {
            SeqNo = 42,
            Node = "test",
            SourceAddr = IPAddress.Loopback.GetAddressBytes(),
            SourcePort = 5000,
            SourceNode = "test"
        };

        var encoded = MessageEncoder.Encode(MessageType.Ping, ping);
        var messages = new[] { encoded, encoded, encoded };
        var compound = MessageEncoder.MakeCompoundMessage(messages);

        // Verify structure
        compound.Should().NotBeEmpty("compound message should not be empty");
        compound[0].Should().Be((byte)MessageType.Compound, "first byte should be compound type");
        compound[1].Should().Be(3, "second byte should be message count");
        
        // Decode and verify round-trip
        var (truncated, decoded) = CompoundMessage.DecodeCompoundMessage(compound.Skip(1).ToArray());
        truncated.Should().Be(0, "no messages should be truncated");
        decoded.Should().HaveCount(3, "should decode 3 messages");
        
        // Verify each decoded message matches original
        for (int i = 0; i < 3; i++)
        {
            decoded[i].Should().Equal(encoded, $"decoded message {i} should match original");
        }
    }

    [Fact]
    public async Task Memberlist_CreateAndShutdown_Works()
    {
        var network = CreateMockNetwork();
        var config = CreateTestConfig("node1");
        config.Transport = network.CreateTransport("node1");
        
        var m = NSerf.Memberlist.Memberlist.Create(config);

        try
        {
            // Verify memberlist was properly initialized
            m.Should().NotBeNull();
            m.NumMembers().Should().Be(1, "should have 1 member (self)");
            
            var localNode = m.LocalNode;
            localNode.Should().NotBeNull();
            localNode.Name.Should().Be("node1", "local node name should match config");
            
            await Task.Delay(10);
        }
        finally
        {
            await m.ShutdownAsync();
            
            // Verify shutdown completed (should not throw on second call)
            var act = async () => await m.ShutdownAsync();
            await act.Should().NotThrowAsync("shutdown should be idempotent");
        }
    }

    [Fact]
    public void MessageEncoder_PingMessage_EncodesDecodes()
    {
        var ping = new PingMessage
        {
            SeqNo = 99,
            Node = "node1",
            SourceAddr = IPAddress.Loopback.GetAddressBytes(),
            SourcePort = 6000,
            SourceNode = "remote"
        };

        var encoded = MessageEncoder.Encode(MessageType.Ping, ping);
        
        // Verify encoding structure
        encoded.Should().NotBeEmpty();
        encoded[0].Should().Be((byte)MessageType.Ping, "first byte should be message type");
        encoded.Length.Should().BeGreaterThan(1, "should have payload after type byte");
        
        // Decode and verify round-trip
        var decoded = MessageEncoder.Decode<PingMessage>(encoded.Skip(1).ToArray());
        decoded.Should().NotBeNull("should decode successfully");
        decoded.SeqNo.Should().Be(99, "SeqNo should match");
        decoded.Node.Should().Be("node1", "Node should match");
        decoded.SourcePort.Should().Be(6000, "SourcePort should match");
        decoded.SourceNode.Should().Be("remote", "SourceNode should match");
    }

    [Fact]
    public async Task AckHandler_InvokeAck_CallsCallback()
    {
        var called = false;
        var seqNo = (uint)123;
        
        var handler = new AckNackHandler(null);
        handler.SetAckHandler(seqNo, (payload, ts) =>
        {
            called = true;
        }, () => { }, TimeSpan.FromSeconds(1));
        
        handler.InvokeAck(seqNo, Array.Empty<byte>(), DateTimeOffset.UtcNow);
        
        await Task.Delay(10);
        
        called.Should().BeTrue("ack handler should be invoked");
    }
    
    [Fact]
    public async Task AckHandler_InvokeNack_CallsCallback()
    {
        var called = false;
        var seqNo = (uint)456;
        
        var handler = new AckNackHandler(null);
        handler.SetAckHandler(seqNo, (payload, ts) => { }, () =>
        {
            called = true;
        }, TimeSpan.FromSeconds(1));
        
        handler.InvokeNack(seqNo);
        
        await Task.Delay(10);
        
        called.Should().BeTrue("nack handler should be invoked");
    }

    [Fact]
    public async Task AckHandler_Timeout_RemovesHandler()
    {
        var seqNo = (uint)789;
        var handler = new AckNackHandler(null);
        var called = false;
        
        handler.SetAckHandler(seqNo, (p, t) => { }, () => 
        {
            called = true;
        }, TimeSpan.FromMilliseconds(50));
        
        await Task.Delay(100);
        handler.InvokeNack(seqNo);
        
        called.Should().BeTrue("timeout should trigger nack");
    }

    [Fact]
    public void MessageEncoder_MultipleTypes_EncodesCorrectly()
    {
        var ping = new PingMessage { SeqNo = 1, Node = "test", SourceNode = "test", SourceAddr = new byte[4], SourcePort = 1000 };
        var encoded = MessageEncoder.Encode(MessageType.Ping, ping);
        
        encoded[0].Should().Be((byte)MessageType.Ping);
        encoded.Length.Should().BeGreaterThan(1);
    }

    [Fact]
    public void CompoundMessage_Empty_HandlesGracefully()
    {
        var compound = MessageEncoder.MakeCompoundMessage(Array.Empty<byte[]>());
        
        // Verify structure of empty compound message
        compound.Should().NotBeEmpty("should have header bytes");
        compound[0].Should().Be((byte)MessageType.Compound, "first byte should be compound type");
        compound[1].Should().Be(0, "second byte should be 0 (no messages)");
        compound.Length.Should().Be(2, "empty compound should only have 2 header bytes");
        
        // Decode and verify
        var (truncated, decoded) = CompoundMessage.DecodeCompoundMessage(compound.Skip(1).ToArray());
        truncated.Should().Be(0, "no truncation");
        decoded.Should().BeEmpty("should have no messages");
    }

    [Fact]
    public void CompoundMessage_Large_HandlesCorrectly()
    {
        var messages = new List<byte[]>();
        for (int i = 0; i < 100; i++)
        {
            var ping = new PingMessage { SeqNo = (uint)i, Node = $"node{i}", SourceNode = "test", SourceAddr = new byte[4], SourcePort = 1000 };
            messages.Add(MessageEncoder.Encode(MessageType.Ping, ping));
        }

        var compound = MessageEncoder.MakeCompoundMessage(messages.ToArray());
        
        // Verify structure
        compound.Should().NotBeEmpty();
        compound[0].Should().Be((byte)MessageType.Compound, "first byte should be compound type");
        compound[1].Should().Be(100, "should have 100 messages");
        
        // Decode and verify all messages recovered
        var (truncated, decoded) = CompoundMessage.DecodeCompoundMessage(compound.Skip(1).ToArray());
        truncated.Should().Be(0, "no messages should be truncated");
        decoded.Should().HaveCount(100, "should decode all 100 messages");
        
        // Verify each message has correct SeqNo
        for (int i = 0; i < 100; i++)
        {
            var decodedPing = MessageEncoder.Decode<PingMessage>(decoded[i].Skip(1).ToArray());
            decodedPing.Should().NotBeNull($"message {i} should decode");
            decodedPing.SeqNo.Should().Be((uint)i, $"message {i} should have correct SeqNo");
        }
    }
}

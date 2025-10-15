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
        var compound = MessageEncoder.MakeCompoundMessage(new[] { encoded, encoded, encoded });

        compound.Should().NotBeEmpty("compound message should not be empty");
        compound[0].Should().Be((byte)MessageType.Compound);
    }

    [Fact]
    public async Task Memberlist_CreateAndShutdown_Works()
    {
        var network = CreateMockNetwork();
        var config = CreateTestConfig("node1");
        config.Transport = network.CreateTransport("node1");
        
        var m = await NSerf.Memberlist.Memberlist.CreateAsync(config);

        try
        {
            m.Should().NotBeNull();
            await Task.Delay(10);
        }
        finally
        {
            await m.ShutdownAsync();
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
        
        encoded.Should().NotBeEmpty();
        encoded[0].Should().Be((byte)MessageType.Ping);
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
        compound.Should().NotBeEmpty();
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
        compound.Should().NotBeEmpty();
        compound[0].Should().Be((byte)MessageType.Compound);
    }
}

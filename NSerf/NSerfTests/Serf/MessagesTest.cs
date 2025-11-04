// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Ported from: github.com/hashicorp/serf/serf/messages_test.go

using NSerf.Serf;
using System.Net;
using MessagePack;

namespace NSerfTests.Serf;

/// <summary>
/// Tests for message encoding, decoding, and serialization.
/// Matches the Go implementation tests exactly.
/// </summary>
public class MessagesTest
{
    [Fact]
    public void QueryFlags_ShouldHaveCorrectValues()
    {
        // Arrange & Act & Assert - Test exact flag values from Go
        ((uint)QueryFlags.Ack).Should().Be(1u, "queryFlagAck should be 1");
        ((uint)QueryFlags.NoBroadcast).Should().Be(2u, "queryFlagNoBroadcast should be 2");
    }

    [Fact]
    public void EncodeMessage_ShouldEncodeAndDecodeMessageLeave()
    {
        // Arrange
        var input = new MessageLeave
        {
            Node = "foo"
        };

        // Act - Encode the message
        var encoded = MessageCodec.EncodeMessage(MessageType.Leave, input);

        // Assert - Check type header
        encoded.Should().NotBeEmpty();
        encoded[0].Should().Be((byte)MessageType.Leave, "first byte should be message type header");

        // Act - Decode the message (skip first byte which is type header)
        var decoded = MessageCodec.DecodeMessage<MessageLeave>(encoded.AsSpan(1).ToArray());

        // Assert - Should match original
        decoded.Should().NotBeNull();
        decoded.Node.Should().Be(input.Node);
        decoded.LTime.Should().Be(input.LTime);
        decoded.Prune.Should().Be(input.Prune);
    }

    [Fact]
    public void EncodeMessage_ShouldEncodeAndDecodeMessageJoin()
    {
        // Arrange
        var input = new MessageJoin
        {
            LTime = 42,
            Node = "test-node"
        };

        // Act - Encode
        var encoded = MessageCodec.EncodeMessage(MessageType.Join, input);

        // Assert - Check type header
        encoded[0].Should().Be((byte)MessageType.Join);

        // Act - Decode
        var decoded = MessageCodec.DecodeMessage<MessageJoin>(encoded.AsSpan(1).ToArray());

        // Assert
        decoded.Node.Should().Be(input.Node);
        decoded.LTime.Should().Be(input.LTime);
    }

    [Fact]
    public void EncodeMessage_ShouldEncodeAndDecodeMessageUserEvent()
    {
        // Arrange
        var input = new MessageUserEvent
        {
            LTime = 100,
            Name = "deploy",
            Payload = new byte[] { 1, 2, 3, 4, 5 },
            CC = true
        };

        // Act - Encode
        var encoded = MessageCodec.EncodeMessage(MessageType.UserEvent, input);

        // Assert - Check type header
        encoded[0].Should().Be((byte)MessageType.UserEvent);

        // Act - Decode
        var decoded = MessageCodec.DecodeMessage<MessageUserEvent>(encoded.AsSpan(1).ToArray());

        // Assert
        decoded.LTime.Should().Be(input.LTime);
        decoded.Name.Should().Be(input.Name);
        decoded.Payload.Should().Equal(input.Payload);
        decoded.CC.Should().Be(input.CC);
    }

    [Fact]
    public void EncodeMessage_ShouldEncodeAndDecodeMessageQuery()
    {
        // Arrange
        var input = new MessageQuery
        {
            LTime = 200,
            ID = 12345,
            Addr = new byte[] { 127, 0, 0, 1 },
            Port = 7946,
            SourceNode = "node1",
            Filters = new List<byte[]> { new byte[] { 1, 2, 3 } },
            Flags = (uint)QueryFlags.Ack,
            RelayFactor = 3,
            Timeout = TimeSpan.FromSeconds(30),
            Name = "health-check",
            Payload = new byte[] { 10, 20, 30 }
        };

        // Act - Encode
        var encoded = MessageCodec.EncodeMessage(MessageType.Query, input);

        // Assert - Check type header
        encoded[0].Should().Be((byte)MessageType.Query);

        // Act - Decode
        var decoded = MessageCodec.DecodeMessage<MessageQuery>(encoded.AsSpan(1).ToArray());

        // Assert
        decoded.LTime.Should().Be(input.LTime);
        decoded.ID.Should().Be(input.ID);
        decoded.Addr.Should().Equal(input.Addr);
        decoded.Port.Should().Be(input.Port);
        decoded.SourceNode.Should().Be(input.SourceNode);
        decoded.Flags.Should().Be(input.Flags);
        decoded.RelayFactor.Should().Be(input.RelayFactor);
        decoded.Timeout.Should().Be(input.Timeout);
        decoded.Name.Should().Be(input.Name);
        decoded.Payload.Should().Equal(input.Payload);
        decoded.IsAck.Should().BeTrue();
        decoded.IsNoBroadcast.Should().BeFalse();
    }

    [Fact]
    public void EncodeMessage_ShouldEncodeAndDecodeMessageQueryResponse()
    {
        // Arrange
        var input = new MessageQueryResponse
        {
            LTime = 250,
            ID = 54321,
            From = "responder-node",
            Flags = (uint)QueryFlags.Ack,
            Payload = new byte[] { 99, 88, 77 }
        };

        // Act - Encode
        var encoded = MessageCodec.EncodeMessage(MessageType.QueryResponse, input);

        // Assert - Check type header
        encoded[0].Should().Be((byte)MessageType.QueryResponse);

        // Act - Decode
        var decoded = MessageCodec.DecodeMessage<MessageQueryResponse>(encoded.AsSpan(1).ToArray());

        // Assert
        decoded.LTime.Should().Be(input.LTime);
        decoded.ID.Should().Be(input.ID);
        decoded.From.Should().Be(input.From);
        decoded.Flags.Should().Be(input.Flags);
        decoded.Payload.Should().Equal(input.Payload);
        decoded.IsAck.Should().BeTrue();
    }

    [Fact]
    public void EncodeRelayMessage_ShouldEncodeWithRelayHeader()
    {
        // Arrange - Matches Go test exactly
        var input = new MessageLeave { Node = "foo" };
        var destAddr = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 1234);
        var nodeName = "test";

        // Act - Encode relay message
        var encoded = MessageCodec.EncodeRelayMessage(MessageType.Leave, destAddr, nodeName, input);

        // Assert - Should have relay type header
        encoded.Should().NotBeEmpty();
        encoded[0].Should().Be((byte)MessageType.Relay, "first byte should be relay message type");

        // Parse the relay header
        using var ms = new MemoryStream(encoded, 1, encoded.Length - 1);
        var header = MessagePackSerializer.Deserialize<RelayHeader>(ms);

        // Assert - Check relay header
        header.Should().NotBeNull();
        header.DestAddr.Should().NotBeNull();
        header.DestAddr.IP.Should().Equal(new byte[] { 127, 0, 0, 1 });
        header.DestAddr.Port.Should().Be(destAddr.Port);
        header.DestName.Should().Be(nodeName);
        
        // Verify ToIPEndPoint works correctly
        var reconstructed = header.ToIpEndPoint();
        reconstructed.Address.Should().Be(destAddr.Address);
        reconstructed.Port.Should().Be(destAddr.Port);

        // Read the actual message type byte
        var messageTypeByte = ms.ReadByte();
        messageTypeByte.Should().Be((byte)MessageType.Leave);

        // Decode the actual message
        var remainingBytes = new byte[ms.Length - ms.Position];
        ms.Read(remainingBytes, 0, remainingBytes.Length);
        var decoded = MessagePackSerializer.Deserialize<MessageLeave>(remainingBytes);

        // Assert - Message should match
        decoded.Should().NotBeNull();
        decoded.Node.Should().Be(input.Node);
    }

    [Fact]
    public void EncodeRelayMessage_ComplexMessage_ShouldPreserveAllFields()
    {
        // Arrange - Test with more complex message
        var input = new MessageQuery
        {
            LTime = 300,
            ID = 99999,
            Addr = new byte[] { 192, 168, 1, 100 },
            Port = 8080,
            SourceNode = "origin",
            Flags = (uint)(QueryFlags.Ack | QueryFlags.NoBroadcast),
            RelayFactor = 5,
            Timeout = TimeSpan.FromMinutes(1),
            Name = "custom-query",
            Payload = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }
        };
        var destAddr = new IPEndPoint(IPAddress.Parse("10.0.0.1"), 9000);

        // Act
        var encoded = MessageCodec.EncodeRelayMessage(MessageType.Query, destAddr, "relay-target", input);

        // Assert
        encoded[0].Should().Be((byte)MessageType.Relay);

        // Parse header
        using var ms = new MemoryStream(encoded, 1, encoded.Length - 1);
        var header = MessagePackSerializer.Deserialize<RelayHeader>(ms);
        header.DestAddr.Port.Should().Be(9000);
        header.DestName.Should().Be("relay-target");

        // Parse message
        ms.ReadByte(); // Skip message type byte
        var remainingBytes = new byte[ms.Length - ms.Position];
        ms.Read(remainingBytes, 0, remainingBytes.Length);
        var decoded = MessagePackSerializer.Deserialize<MessageQuery>(remainingBytes);

        decoded.LTime.Should().Be(input.LTime);
        decoded.ID.Should().Be(input.ID);
        decoded.SourceNode.Should().Be(input.SourceNode);
        decoded.IsAck.Should().BeTrue();
        decoded.IsNoBroadcast.Should().BeTrue();
    }

    [Fact]
    public void EncodeFilter_NodeFilter_ShouldEncodeAndDecode()
    {
        // Arrange - Matches Go test exactly
        var nodes = new List<string> { "foo", "bar" };

        // Act - Encode filter
        var encoded = MessageCodec.EncodeFilter(FilterType.Node, nodes);

        // Assert - Check type header
        encoded.Should().NotBeEmpty();
        encoded[0].Should().Be((byte)FilterType.Node, "first byte should be filter type header");

        // Act - Decode (skip first byte)
        var decoded = MessageCodec.DecodeMessage<List<string>>(encoded.AsSpan(1).ToArray());

        // Assert - Should match original
        decoded.Should().NotBeNull();
        decoded.Should().HaveCount(2);
        decoded.Should().Equal(nodes);
    }

    [Fact]
    public void EncodeFilter_TagFilter_ShouldEncodeAndDecode()
    {
        // Arrange
        var tagFilter = new FilterTag
        {
            Tag = "role",
            Expr = "web.*"
        };

        // Act - Encode
        var encoded = MessageCodec.EncodeFilter(FilterType.Tag, tagFilter);

        // Assert - Check type header
        encoded[0].Should().Be((byte)FilterType.Tag);

        // Act - Decode
        var decoded = MessageCodec.DecodeMessage<FilterTag>(encoded.AsSpan(1).ToArray());

        // Assert
        decoded.Tag.Should().Be(tagFilter.Tag);
        decoded.Expr.Should().Be(tagFilter.Expr);
    }

    [Fact]
    public void EncodeFilter_EmptyNodeList_ShouldEncode()
    {
        // Arrange
        var nodes = new List<string>();

        // Act
        var encoded = MessageCodec.EncodeFilter(FilterType.Node, nodes);

        // Assert
        encoded[0].Should().Be((byte)FilterType.Node);
        var decoded = MessageCodec.DecodeMessage<List<string>>(encoded.AsSpan(1).ToArray());
        decoded.Should().BeEmpty();
    }

    [Fact]
    public void MessageQuery_FlagHelpers_ShouldWorkCorrectly()
    {
        // Arrange & Act - No flags
        var query1 = new MessageQuery { Flags = 0 };

        // Assert
        query1.IsAck.Should().BeFalse();
        query1.IsNoBroadcast.Should().BeFalse();

        // Arrange & Act - Ack flag only
        var query2 = new MessageQuery { Flags = (uint)QueryFlags.Ack };

        // Assert
        query2.IsAck.Should().BeTrue();
        query2.IsNoBroadcast.Should().BeFalse();

        // Arrange & Act - NoBroadcast flag only
        var query3 = new MessageQuery { Flags = (uint)QueryFlags.NoBroadcast };

        // Assert
        query3.IsAck.Should().BeFalse();
        query3.IsNoBroadcast.Should().BeTrue();

        // Arrange & Act - Both flags
        var query4 = new MessageQuery { Flags = (uint)(QueryFlags.Ack | QueryFlags.NoBroadcast) };

        // Assert
        query4.IsAck.Should().BeTrue();
        query4.IsNoBroadcast.Should().BeTrue();
    }

    [Fact]
    public void MessageQueryResponse_AckFlag_ShouldWorkCorrectly()
    {
        // Arrange & Act - No ack
        var response1 = new MessageQueryResponse { Flags = 0 };

        // Assert
        response1.IsAck.Should().BeFalse();

        // Arrange & Act - With ack
        var response2 = new MessageQueryResponse { Flags = (uint)QueryFlags.Ack };

        // Assert
        response2.IsAck.Should().BeTrue();
    }

    [Fact]
    public void MessagePushPull_ShouldEncodeAndDecode()
    {
        // Arrange - Test the largest/most complex message type
        var input = new MessagePushPull
        {
            LTime = 500,
            StatusLTimes = new Dictionary<string, LamportTime>
            {
                ["node1"] = 100,
                ["node2"] = 150,
                ["node3"] = 200
            },
            LeftMembers = new List<string> { "old-node1", "old-node2" },
            EventLTime = 450,
            Events = new List<UserEventCollection>
            {
                new UserEventCollection
                {
                    LTime = 400,
                    Events = new List<UserEventData>
                    {
                        new UserEventData { Name = "event1", Payload = new byte[] { 1, 2 } },
                        new UserEventData { Name = "event2", Payload = new byte[] { 3, 4 } }
                    }
                }
            },
            QueryLTime = 475
        };

        // Act - Encode
        var encoded = MessageCodec.EncodeMessage(MessageType.PushPull, input);

        // Assert - Check type header
        encoded[0].Should().Be((byte)MessageType.PushPull);

        // Act - Decode
        var decoded = MessageCodec.DecodeMessage<MessagePushPull>(encoded.AsSpan(1).ToArray());

        // Assert
        decoded.LTime.Should().Be(input.LTime);
        decoded.StatusLTimes.Should().HaveCount(3);
        decoded.StatusLTimes["node1"].Should().Be((LamportTime)100);
        decoded.StatusLTimes["node2"].Should().Be((LamportTime)150);
        decoded.StatusLTimes["node3"].Should().Be((LamportTime)200);
        decoded.LeftMembers.Should().Equal(input.LeftMembers);
        decoded.EventLTime.Should().Be(input.EventLTime);
        decoded.Events.Should().HaveCount(1);
        decoded.Events[0].LTime.Should().Be((LamportTime)400);
        decoded.Events[0].Events.Should().HaveCount(2);
        decoded.QueryLTime.Should().Be(input.QueryLTime);
    }

    [Fact]
    public void MessageType_AllTypes_ShouldHaveCorrectValues()
    {
        // Assert - Verify all message type values match Go constants
        ((byte)MessageType.Leave).Should().Be(0);
        ((byte)MessageType.Join).Should().Be(1);
        ((byte)MessageType.PushPull).Should().Be(2);
        ((byte)MessageType.UserEvent).Should().Be(3);
        ((byte)MessageType.Query).Should().Be(4);
        ((byte)MessageType.QueryResponse).Should().Be(5);
        ((byte)MessageType.ConflictResponse).Should().Be(6);
        ((byte)MessageType.KeyRequest).Should().Be(7);
        ((byte)MessageType.KeyResponse).Should().Be(8);
        ((byte)MessageType.Relay).Should().Be(9);
    }

    [Fact]
    public void FilterType_AllTypes_ShouldHaveCorrectValues()
    {
        // Assert - Verify filter type values
        ((byte)FilterType.Node).Should().Be(0);
        ((byte)FilterType.Tag).Should().Be(1);
    }

    [Fact]
    public void EncodeMessage_EmptyPayload_ShouldWork()
    {
        // Arrange
        var input = new MessageUserEvent
        {
            LTime = 1,
            Name = "empty-event",
            Payload = Array.Empty<byte>(),
            CC = false
        };

        // Act
        var encoded = MessageCodec.EncodeMessage(MessageType.UserEvent, input);
        var decoded = MessageCodec.DecodeMessage<MessageUserEvent>(encoded.AsSpan(1).ToArray());

        // Assert
        decoded.Name.Should().Be(input.Name);
        decoded.Payload.Should().BeEmpty();
    }

    [Fact]
    public void EncodeMessage_LargePayload_ShouldWork()
    {
        // Arrange - Test with larger payload
        var largePayload = new byte[10000];
        for (int i = 0; i < largePayload.Length; i++)
        {
            largePayload[i] = (byte)(i % 256);
        }

        var input = new MessageUserEvent
        {
            LTime = 999,
            Name = "large-event",
            Payload = largePayload,
            CC = true
        };

        // Act
        var encoded = MessageCodec.EncodeMessage(MessageType.UserEvent, input);
        var decoded = MessageCodec.DecodeMessage<MessageUserEvent>(encoded.AsSpan(1).ToArray());

        // Assert
        decoded.Payload.Should().HaveCount(10000);
        decoded.Payload.Should().Equal(largePayload);
    }

    [Fact]
    public void EncodeFilter_MultipleNodes_ShouldPreserveOrder()
    {
        // Arrange
        var nodes = new List<string> { "alpha", "beta", "gamma", "delta", "epsilon" };

        // Act
        var encoded = MessageCodec.EncodeFilter(FilterType.Node, nodes);
        var decoded = MessageCodec.DecodeMessage<List<string>>(encoded.AsSpan(1).ToArray());

        // Assert - Order should be preserved
        decoded.Should().Equal(nodes);
    }

    [Fact]
    public void MessageLeave_WithPruneFlag_ShouldEncodeCorrectly()
    {
        // Arrange
        var input = new MessageLeave
        {
            LTime = 777,
            Node = "departing-node",
            Prune = true
        };

        // Act
        var encoded = MessageCodec.EncodeMessage(MessageType.Leave, input);
        var decoded = MessageCodec.DecodeMessage<MessageLeave>(encoded.AsSpan(1).ToArray());

        // Assert
        decoded.Node.Should().Be(input.Node);
        decoded.LTime.Should().Be(input.LTime);
        decoded.Prune.Should().BeTrue();
    }
}

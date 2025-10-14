// Ported from: github.com/hashicorp/memberlist/mock_transport.go
// Copyright (c) HashiCorp, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Memberlist.Transport;

namespace NSerfTests.Memberlist.Transport;

public class MockTransportTests
{
    [Fact]
    public void MockNetwork_CreateTransport_ShouldCreateUniqueTransport()
    {
        // Arrange
        var network = new MockNetwork();
        
        // Act
        var transport1 = network.CreateTransport("node1");
        var transport2 = network.CreateTransport("node2");
        
        // Assert
        transport1.Should().NotBeNull();
        transport2.Should().NotBeNull();
        transport1.Should().NotBe(transport2);
    }
    
    [Fact]
    public async Task MockTransport_WriteToAddress_ShouldDeliverPacket()
    {
        // Arrange
        var network = new MockNetwork();
        var transport1 = network.CreateTransport("node1");
        var transport2 = network.CreateTransport("node2");
        
        var testData = "Hello, World!"u8.ToArray();
        
        // Act
        var (ip, port) = transport2.FinalAdvertiseAddr("", 0);
        var addr = new Address { Addr = $"{ip}:{port}", Name = string.Empty };
        
        var sendTime = await transport1.WriteToAddressAsync(testData, addr);
        
        // Assert
        sendTime.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
        
        // Read the packet from transport2
        var packet = await transport2.PacketChannel.ReadAsync();
        packet.Buf.Should().BeEquivalentTo(testData);
        packet.Timestamp.Should().Be(sendTime);
    }
    
    [Fact]
    public async Task MockTransport_WriteToAddressByName_ShouldDeliverPacket()
    {
        // Arrange
        var network = new MockNetwork();
        var transport1 = network.CreateTransport("node1");
        var transport2 = network.CreateTransport("node2");
        
        var testData = "Test message"u8.ToArray();
        
        // Act - Send to node2 by name
        var addr = new Address { Addr = string.Empty, Name = "node2" };
        await transport1.WriteToAddressAsync(testData, addr);
        
        // Assert
        var packet = await transport2.PacketChannel.ReadAsync();
        packet.Buf.Should().BeEquivalentTo(testData);
    }
    
    [Fact]
    public async Task MockTransport_WriteToInvalidAddress_ShouldThrow()
    {
        // Arrange
        var network = new MockNetwork();
        var transport1 = network.CreateTransport("node1");
        
        var testData = "Test"u8.ToArray();
        var invalidAddr = new Address { Addr = "invalid:9999", Name = string.Empty };
        
        // Act
        Func<Task> act = async () => await transport1.WriteToAddressAsync(testData, invalidAddr);
        
        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No route*");
    }
    
    [Fact]
    public async Task MockTransport_DialAddressTimeout_ShouldCreateConnection()
    {
        // Arrange
        var network = new MockNetwork();
        var transport1 = network.CreateTransport("node1");
        var transport2 = network.CreateTransport("node2");
        
        // Act - Dial from transport1 to transport2
        var (ip, port) = transport2.FinalAdvertiseAddr("", 0);
        var addr = new Address { Addr = $"{ip}:{port}", Name = string.Empty };
        
        var dialTask = transport1.DialAddressTimeoutAsync(addr, TimeSpan.FromSeconds(1));
        var acceptTask = transport2.StreamChannel.ReadAsync().AsTask();
        
        await Task.WhenAll(dialTask, acceptTask);
        
        var clientStream = await dialTask;
        var serverStream = await acceptTask;
        
        // Assert - Both streams should be connected
        clientStream.Should().NotBeNull();
        serverStream.Should().NotBeNull();
        
        // Test bidirectional communication
        var testData = "Hello from client"u8.ToArray();
        await clientStream.WriteAsync(testData);
        await clientStream.FlushAsync();
        
        var buffer = new byte[100];
        var bytesRead = await serverStream.ReadAsync(buffer);
        buffer.AsSpan(0, bytesRead).ToArray().Should().BeEquivalentTo(testData);
    }
    
    [Fact]
    public async Task MockTransport_BidirectionalStream_ShouldWork()
    {
        // Arrange
        var network = new MockNetwork();
        var transport1 = network.CreateTransport("node1");
        var transport2 = network.CreateTransport("node2");
        
        var (ip, port) = transport2.FinalAdvertiseAddr("", 0);
        var addr = new Address { Addr = $"{ip}:{port}", Name = string.Empty };
        
        // Act - Establish connection
        var dialTask = transport1.DialAddressTimeoutAsync(addr, TimeSpan.FromSeconds(1));
        var acceptTask = transport2.StreamChannel.ReadAsync().AsTask();
        
        var clientStream = await dialTask;
        var serverStream = await acceptTask;
        
        // Send from client to server
        var clientData = "Client message"u8.ToArray();
        await clientStream.WriteAsync(clientData);
        await clientStream.FlushAsync();
        
        var serverBuffer = new byte[100];
        var serverBytesRead = await serverStream.ReadAsync(serverBuffer);
        serverBuffer.AsSpan(0, serverBytesRead).ToArray().Should().BeEquivalentTo(clientData);
        
        // Send from server to client
        var serverData = "Server response"u8.ToArray();
        await serverStream.WriteAsync(serverData);
        await serverStream.FlushAsync();
        
        var clientBuffer = new byte[100];
        var clientBytesRead = await clientStream.ReadAsync(clientBuffer);
        clientBuffer.AsSpan(0, clientBytesRead).ToArray().Should().BeEquivalentTo(serverData);
    }
    
    [Fact]
    public void MockTransport_FinalAdvertiseAddr_ShouldReturnCorrectAddress()
    {
        // Arrange
        var network = new MockNetwork();
        var transport = network.CreateTransport("test-node");
        
        // Act
        var (ip, port) = transport.FinalAdvertiseAddr("", 0);
        
        // Assert
        ip.Should().Be(System.Net.IPAddress.Parse("127.0.0.1"));
        port.Should().BeGreaterThan(20000);
    }
    
    [Fact]
    public async Task MockTransport_Shutdown_ShouldCompleteChannels()
    {
        // Arrange
        var network = new MockNetwork();
        var transport = network.CreateTransport("node1");
        
        // Act
        await transport.ShutdownAsync();
        
        // Assert - Channels should be completed
        var packetReadTask = transport.PacketChannel.ReadAsync();
        await Task.Delay(100); // Give it time
        packetReadTask.IsCompleted.Should().BeTrue();
    }
}

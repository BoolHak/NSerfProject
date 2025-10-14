// Ported from: github.com/hashicorp/memberlist/state.go
// Copyright (c) HashiCorp, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.Net;
using NSerf.Memberlist.State;

namespace NSerfTests.Memberlist.State;

public class NodeTests
{
    [Fact]
    public void Node_Address_ShouldReturnFormattedAddress()
    {
        // Arrange
        var node = new Node
        {
            Name = "test-node",
            Addr = IPAddress.Parse("192.168.1.100"),
            Port = 7946
        };
        
        // Act
        var address = node.Address();
        
        // Assert
        address.Should().Be("192.168.1.100:7946");
    }
    
    [Fact]
    public void Node_Address_ShouldHandleIPv6()
    {
        // Arrange
        var node = new Node
        {
            Name = "test-node",
            Addr = IPAddress.Parse("::1"),
            Port = 8080
        };
        
        // Act
        var address = node.Address();
        
        // Assert
        address.Should().Be("[::1]:8080");
    }
    
    [Fact]
    public void Node_FullAddress_ShouldReturnAddressWithName()
    {
        // Arrange
        var node = new Node
        {
            Name = "test-node",
            Addr = IPAddress.Parse("192.168.1.100"),
            Port = 7946
        };
        
        // Act
        var fullAddress = node.FullAddress();
        
        // Assert
        fullAddress.Name.Should().Be("test-node");
        fullAddress.Addr.Should().Be("192.168.1.100:7946");
    }
    
    [Fact]
    public void Node_ToString_ShouldReturnNodeName()
    {
        // Arrange
        var node = new Node
        {
            Name = "test-node",
            Addr = IPAddress.Parse("192.168.1.100"),
            Port = 7946
        };
        
        // Act
        var str = node.ToString();
        
        // Assert
        str.Should().Be("test-node");
    }
    
    [Fact]
    public void Node_ShouldStoreMetadata()
    {
        // Arrange
        var metadata = new byte[] { 1, 2, 3, 4, 5 };
        var node = new Node
        {
            Name = "test-node",
            Addr = IPAddress.Parse("192.168.1.100"),
            Port = 7946,
            Meta = metadata
        };
        
        // Assert
        node.Meta.Should().BeEquivalentTo(metadata);
    }
    
    [Fact]
    public void Node_ShouldStoreProtocolVersions()
    {
        // Arrange
        var node = new Node
        {
            Name = "test-node",
            Addr = IPAddress.Parse("192.168.1.100"),
            Port = 7946,
            PMin = 1,
            PMax = 5,
            PCur = 5,
            DMin = 2,
            DMax = 5,
            DCur = 5
        };
        
        // Assert
        node.PMin.Should().Be(1);
        node.PMax.Should().Be(5);
        node.PCur.Should().Be(5);
        node.DMin.Should().Be(2);
        node.DMax.Should().Be(5);
        node.DCur.Should().Be(5);
    }
}

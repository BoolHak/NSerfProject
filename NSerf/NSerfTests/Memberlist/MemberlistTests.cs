// Ported from: github.com/hashicorp/memberlist/memberlist.go
// Copyright (c) HashiCorp, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Memberlist;
using NSerf.Memberlist.Configuration;
using NSerfTests.Memberlist.Transport;

namespace NSerfTests.Memberlist;

public class MemberlistTests
{
    [Fact]
    public async Task Create_WithValidConfig_ShouldSucceed()
    {
        // Arrange
        var config = MemberlistConfig.DefaultLocalConfig();
        config.Name = "test-node";
        
        var network = new MockNetwork();
        config.Transport = network.CreateTransport("test-node");
        
        // Act
        var memberlist = NSerf.Memberlist.Memberlist.Create(config);
        
        // Assert
        memberlist.Should().NotBeNull();
        memberlist.LocalNode.Should().NotBeNull();
        memberlist.LocalNode.Name.Should().Be("test-node");
        
        // Cleanup
        await memberlist.ShutdownAsync();
    }
    
    [Fact]
    public void Create_WithInvalidProtocolVersion_ShouldThrow()
    {
        // Arrange
        var config = MemberlistConfig.DefaultLocalConfig();
        config.ProtocolVersion = 0; // Too low
        
        var network = new MockNetwork();
        config.Transport = network.CreateTransport("test-node");
        
        // Act
        var act =  () =>  NSerf.Memberlist.Memberlist.Create(config);
        
        // Assert
        act.Should().Throw<ArgumentException>().WithMessage("*protocol version*");
    }
    
    [Fact]
    public async Task LocalNode_ShouldReturnConfiguredNode()
    {
        // Arrange
        var config = MemberlistConfig.DefaultLocalConfig();
        config.Name = "my-node";
        
        var network = new MockNetwork();
        config.Transport = network.CreateTransport("my-node");
        
        // Act
        var memberlist = NSerf.Memberlist.Memberlist.Create(config);
        
        // Assert
        var localNode = memberlist.LocalNode;
        localNode.Name.Should().Be("my-node");
        localNode.Addr.Should().NotBeNull();
        localNode.Port.Should().BeGreaterThan(0);
        
        // Cleanup
        await memberlist.ShutdownAsync();
    }
    
    [Fact]
    public async Task NumMembers_InitiallyShouldBe1()
    {
        // Arrange
        var config = MemberlistConfig.DefaultLocalConfig();
        config.Name = "solo-node";
        
        var network = new MockNetwork();
        config.Transport = network.CreateTransport("solo-node");
        
        // Act
        var memberlist = NSerf.Memberlist.Memberlist.Create(config);
        
        // Assert
        memberlist.NumMembers().Should().Be(1, "only local node should be in the cluster");
        
        // Cleanup
        await memberlist.ShutdownAsync();
    }
    
    [Fact]
    public async Task GetHealthScore_ShouldReturnZero_Initially()
    {
        // Arrange
        var config = MemberlistConfig.DefaultLocalConfig();
        config.Name = "healthy-node";
        
        var network = new MockNetwork();
        config.Transport = network.CreateTransport("healthy-node");
        
        // Act
        var memberlist = NSerf.Memberlist.Memberlist.Create(config);
        
        // Assert
        memberlist.GetHealthScore().Should().Be(0, "new node should be perfectly healthy");
        
        // Cleanup
        await memberlist.ShutdownAsync();
    }
    
    [Fact]
    public async Task Shutdown_ShouldCleanlyShutdown()
    {
        // Arrange
        var config = MemberlistConfig.DefaultLocalConfig();
        config.Name = "shutdown-node";
        
        var network = new MockNetwork();
        config.Transport = network.CreateTransport("shutdown-node");
        
        var memberlist = NSerf.Memberlist.Memberlist.Create(config);
        
        // Act
        await memberlist.ShutdownAsync();
        
        // Assert - Should not throw on double shutdown
        Func<Task> act = async () => await memberlist.ShutdownAsync();
        await act.Should().NotThrowAsync("double shutdown should be safe");
    }
}

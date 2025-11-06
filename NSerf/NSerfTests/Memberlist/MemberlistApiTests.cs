// Ported from: github.com/hashicorp/memberlist/memberlist_test.go
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.Net;
using FluentAssertions;
using NSerf.Memberlist;
using NSerf.Memberlist.Configuration;
using NSerf.Memberlist.Messages;
using NSerf.Memberlist.State;
using NSerfTests.Memberlist.Transport;
using Xunit;

namespace NSerfTests.Memberlist;

public class MemberlistApiTests
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
    public async Task Create_DefaultConfig_Succeeds()
    {
        var network = CreateMockNetwork();
        var config = CreateTestConfig("test");
        config.Transport = network.CreateTransport("test");
        
        var m = NSerf.Memberlist.Memberlist.Create(config);

        try
        {
            // Verify memberlist was properly initialized
            m.Should().NotBeNull();
            
            // Verify local node is configured correctly
            m.LocalNode.Name.Should().Be("test");
            m.LocalNode.Addr.Should().NotBeNull("local node should have address");
            m.LocalNode.State.Should().Be(NodeStateType.Alive, "local node should be alive");
            
            // Verify memberlist has only self
            m.NumMembers().Should().Be(1, "newly created memberlist should have only self");
            m.EstNumNodes().Should().Be(1, "estimated nodes should be 1");
            
            // Verify initial health score is 0
            m.GetHealthScore().Should().Be(0, "initial health score should be 0");
            
            // Verify broadcast queue is initialized
            m.Broadcasts.Should().NotBeNull();
            m.Broadcasts.NumQueued().Should().Be(0, "initial broadcast queue should be empty");
        }
        finally
        {
            await m.ShutdownAsync();
        }
    }

    [Fact]
    public async Task NodeMap_InitialState_ContainsSelf()
    {
        var network = CreateMockNetwork();
        var config = CreateTestConfig("node1");
        config.Transport = network.CreateTransport("node1");
        
        var m = NSerf.Memberlist.Memberlist.Create(config);

        try
        {
            m.NodeMap.Should().ContainKey("node1");
            m.Nodes.Should().ContainSingle();
        }
        finally
        {
            await m.ShutdownAsync();
        }
    }

    [Fact]
    public async Task NumMembers_InitialState_ReturnsOne()
    {
        var network = CreateMockNetwork();
        var config = CreateTestConfig("node1");
        config.Transport = network.CreateTransport("node1");
        
        var m = NSerf.Memberlist.Memberlist.Create(config);

        try
        {
            m.NumMembers().Should().Be(1);
        }
        finally
        {
            await m.ShutdownAsync();
        }
    }

    [Fact]
    public async Task EstimatedNumNodes_InitialState_ReturnsOne()
    {
        var network = CreateMockNetwork();
        var config = CreateTestConfig("node1");
        config.Transport = network.CreateTransport("node1");
        
        var m = NSerf.Memberlist.Memberlist.Create(config);

        try
        {
            m.EstNumNodes().Should().Be(1);
        }
        finally
        {
            await m.ShutdownAsync();
        }
    }

    [Fact]
    public async Task LocalNode_ReturnsCorrectInfo()
    {
        var network = CreateMockNetwork();
        var config = CreateTestConfig("mynode");
        config.Transport = network.CreateTransport("mynode");
        
        var m = NSerf.Memberlist.Memberlist.Create(config);

        try
        {
            var local = m.LocalNode;
            local.Name.Should().Be("mynode");
            local.Addr.Should().NotBeNull();
        }
        finally
        {
            await m.ShutdownAsync();
        }
    }

    [Fact]
    public async Task GetHealthScore_InitialState_ReturnsZero()
    {
        var network = CreateMockNetwork();
        var config = CreateTestConfig("node1");
        config.Transport = network.CreateTransport("node1");
        
        var m = NSerf.Memberlist.Memberlist.Create(config);

        try
        {
            m.GetHealthScore().Should().Be(0);
        }
        finally
        {
            await m.ShutdownAsync();
        }
    }

    [Fact]
    public async Task Shutdown_MultipleStates_Succeeds()
    {
        var network = CreateMockNetwork();
        var config = CreateTestConfig("node1");
        config.Transport = network.CreateTransport("node1");
        
        var m = NSerf.Memberlist.Memberlist.Create(config);
        
        // Verify initial state before shutdown
        m.NumMembers().Should().Be(1, "should have 1 member before shutdown");

        // First shutdown
        await m.ShutdownAsync();
        
        // Second shutdown should be idempotent (no throw, no errors)
        var act = async () => await m.ShutdownAsync();
        await act.Should().NotThrowAsync("second shutdown should be idempotent");
        
        // Third shutdown to further verify idempotency
        await act.Should().NotThrowAsync("third shutdown should also be idempotent");
    }

    [Fact]
    public async Task ProtocolVersion_MatchesConfig()
    {
        var network = CreateMockNetwork();
        var config = CreateTestConfig("node1");
        config.Transport = network.CreateTransport("node1");
        config.ProtocolVersion = 3;
        
        var m = NSerf.Memberlist.Memberlist.Create(config);

        try
        {
            m.LocalNode.PCur.Should().Be(3);
        }
        finally
        {
            await m.ShutdownAsync();
        }
    }

    [Fact]
    public async Task Incarnation_Increments()
    {
        var network = CreateMockNetwork();
        var config = CreateTestConfig("node1");
        config.Transport = network.CreateTransport("node1");
        
        var m = NSerf.Memberlist.Memberlist.Create(config);

        try
        {
            var inc1 = m.Incarnation;
            m.NextIncarnation();
            var inc2 = m.Incarnation;
            
            inc2.Should().BeGreaterThan(inc1);
        }
        finally
        {
            await m.ShutdownAsync();
        }
    }

    [Fact]
    public async Task Config_Validation_Succeeds()
    {
        var network = CreateMockNetwork();
        var config = CreateTestConfig("node1");
        config.Transport = network.CreateTransport("node1");
        config.ProtocolVersion = 5;
        
        var m = NSerf.Memberlist.Memberlist.Create(config);

        try
        {
            m.Should().NotBeNull();
        }
        finally
        {
            await m.ShutdownAsync();
        }
    }

    [Fact]
    public async Task MultipleNodes_EstNumNodes_TracksCorrectly()
    {
        var network = CreateMockNetwork();
        var config = CreateTestConfig("node1");
        config.Transport = network.CreateTransport("node1");
        
        var m = NSerf.Memberlist.Memberlist.Create(config);

        try
        {
            var stateHandler = new StateHandlers(m, config.Logger);

            for (int i = 2; i <= 5; i++)
            {
                var alive = new Alive
                {
                    Node = $"node{i}",
                    Addr = IPAddress.Parse($"10.0.0.{i}").GetAddressBytes(),
                    Port = 7946,
                    Incarnation = 1,
                    Meta = Array.Empty<byte>(),
                    Vsn = new byte[] { 
                        ProtocolVersion.Min, ProtocolVersion.Max, config.ProtocolVersion,
                        config.DelegateProtocolMin, config.DelegateProtocolMax, config.DelegateProtocolVersion
                    }
                };
                stateHandler.HandleAliveNode(alive, false, null);
            }

            m.EstNumNodes().Should().Be(5);
            m.NumMembers().Should().Be(5);
        }
        finally
        {
            await m.ShutdownAsync();
        }
    }

    [Fact]
    public async Task GetAdvertiseAddr_ReturnsValidAddress()
    {
        var network = CreateMockNetwork();
        var config = CreateTestConfig("node1");
        config.Transport = network.CreateTransport("node1");
        
        var m = NSerf.Memberlist.Memberlist.Create(config);

        try
        {
            var (addr, port) = m.GetAdvertiseAddr();
            addr.Should().NotBeNull();
            port.Should().BeGreaterThan(0);
        }
        finally
        {
            await m.ShutdownAsync();
        }
    }

    [Fact]
    public async Task NodeState_AfterCreation_SelfIsAlive()
    {
        var network = CreateMockNetwork();
        var config = CreateTestConfig("testnode");
        config.Transport = network.CreateTransport("testnode");
        
        var m = NSerf.Memberlist.Memberlist.Create(config);

        try
        {
            m.NodeMap.TryGetValue("testnode", out var state).Should().BeTrue();
            state!.State.Should().Be(NodeStateType.Alive);
        }
        finally
        {
            await m.ShutdownAsync();
        }
    }

    [Fact]
    public async Task SequenceNum_Increments_Correctly()
    {
        var network = CreateMockNetwork();
        var config = CreateTestConfig("node1");
        config.Transport = network.CreateTransport("node1");
        
        var m = NSerf.Memberlist.Memberlist.Create(config);

        try
        {
            var seq1 = m.NextSequenceNum();
            var seq2 = m.NextSequenceNum();
            
            seq2.Should().Be(seq1 + 1);
        }
        finally
        {
            await m.ShutdownAsync();
        }
    }

    [Fact]
    public async Task Config_CustomValues_Applied()
    {
        var network = CreateMockNetwork();
        var config = CreateTestConfig("custom");
        config.Transport = network.CreateTransport("custom");
        config.SuspicionMult = 10;
        config.ProbeInterval = TimeSpan.FromSeconds(5);
        
        var m = NSerf.Memberlist.Memberlist.Create(config);

        try
        {
            // Verify memberlist was created
            m.Should().NotBeNull();
            
            // Verify custom config values are stored and accessible
            m.Config.SuspicionMult.Should().Be(10, "custom SuspicionMult should be applied");
            m.Config.ProbeInterval.Should().Be(TimeSpan.FromSeconds(5), "custom ProbeInterval should be applied");
            m.Config.Name.Should().Be("custom", "custom name should be applied");
            
            // Verify memberlist is functional with custom config
            m.NumMembers().Should().Be(1, "memberlist should be functional");
            m.LocalNode.Name.Should().Be("custom");
        }
        finally
        {
            await m.ShutdownAsync();
        }
    }

    [Fact]
    public async Task AckHandlers_InitiallyEmpty()
    {
        var network = CreateMockNetwork();
        var config = CreateTestConfig("node1");
        config.Transport = network.CreateTransport("node1");
        
        var m = NSerf.Memberlist.Memberlist.Create(config);

        try
        {
            m.AckHandlers.Should().BeEmpty();
        }
        finally
        {
            await m.ShutdownAsync();
        }
    }

    [Fact]
    public async Task Broadcasts_InitiallyEmpty()
    {
        var network = CreateMockNetwork();
        var config = CreateTestConfig("node1");
        config.Transport = network.CreateTransport("node1");
        
        var m = NSerf.Memberlist.Memberlist.Create(config);

        try
        {
            m.Broadcasts.NumQueued().Should().Be(0);
        }
        finally
        {
            await m.ShutdownAsync();
        }
    }
}

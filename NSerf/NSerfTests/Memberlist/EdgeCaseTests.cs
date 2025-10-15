// Edge case and boundary tests for Memberlist
// Copyright (c) HashiCorp, Inc.
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

public class EdgeCaseTests
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
    public async Task EmptyNodeName_IsAdded()
    {
        var network = CreateMockNetwork();
        var config = CreateTestConfig("node1");
        config.Transport = network.CreateTransport("node1");
        
        var m = await NSerf.Memberlist.Memberlist.CreateAsync(config);

        try
        {
            var stateHandler = new StateHandlers(m, config.Logger);
            
            var alive = new Alive
            {
                Node = "",
                Addr = IPAddress.Parse("10.0.0.1").GetAddressBytes(),
                Port = 7946,
                Incarnation = 1,
                Meta = Array.Empty<byte>(),
                Vsn = new byte[] { 
                    ProtocolVersion.Min, ProtocolVersion.Max, config.ProtocolVersion,
                    config.DelegateProtocolMin, config.DelegateProtocolMax, config.DelegateProtocolVersion
                }
            };
            
            stateHandler.HandleAliveNode(alive, false, null);
            
            m._nodeMap.Should().ContainKey("");
        }
        finally
        {
            await m.ShutdownAsync();
        }
    }

    [Fact]
    public async Task NullMetadata_HandlesGracefully()
    {
        var network = CreateMockNetwork();
        var config = CreateTestConfig("node1");
        config.Transport = network.CreateTransport("node1");
        
        var m = await NSerf.Memberlist.Memberlist.CreateAsync(config);

        try
        {
            var stateHandler = new StateHandlers(m, config.Logger);
            
            var alive = new Alive
            {
                Node = "test-node",
                Addr = IPAddress.Parse("10.0.0.1").GetAddressBytes(),
                Port = 7946,
                Incarnation = 1,
                Meta = null!,
                Vsn = new byte[] { 
                    ProtocolVersion.Min, ProtocolVersion.Max, config.ProtocolVersion,
                    config.DelegateProtocolMin, config.DelegateProtocolMax, config.DelegateProtocolVersion
                }
            };
            
            stateHandler.HandleAliveNode(alive, false, null);
            
            m._nodeMap.TryGetValue("test-node", out _).Should().BeTrue();
        }
        finally
        {
            await m.ShutdownAsync();
        }
    }

    [Fact]
    public async Task VeryHighIncarnation_HandlesCorrectly()
    {
        var network = CreateMockNetwork();
        var config = CreateTestConfig("node1");
        config.Transport = network.CreateTransport("node1");
        
        var m = await NSerf.Memberlist.Memberlist.CreateAsync(config);

        try
        {
            var stateHandler = new StateHandlers(m, config.Logger);
            
            var alive = new Alive
            {
                Node = "high-inc-node",
                Addr = IPAddress.Parse("10.0.0.1").GetAddressBytes(),
                Port = 7946,
                Incarnation = uint.MaxValue - 100,
                Meta = Array.Empty<byte>(),
                Vsn = new byte[] { 
                    ProtocolVersion.Min, ProtocolVersion.Max, config.ProtocolVersion,
                    config.DelegateProtocolMin, config.DelegateProtocolMax, config.DelegateProtocolVersion
                }
            };
            
            stateHandler.HandleAliveNode(alive, false, null);
            
            m._nodeMap["high-inc-node"].Incarnation.Should().Be(uint.MaxValue - 100);
        }
        finally
        {
            await m.ShutdownAsync();
        }
    }

    [Fact]
    public async Task ZeroIncarnation_Accepted()
    {
        var network = CreateMockNetwork();
        var config = CreateTestConfig("node1");
        config.Transport = network.CreateTransport("node1");
        
        var m = await NSerf.Memberlist.Memberlist.CreateAsync(config);

        try
        {
            var stateHandler = new StateHandlers(m, config.Logger);
            
            var alive = new Alive
            {
                Node = "zero-inc",
                Addr = IPAddress.Parse("10.0.0.1").GetAddressBytes(),
                Port = 7946,
                Incarnation = 0,
                Meta = Array.Empty<byte>(),
                Vsn = new byte[] { 
                    ProtocolVersion.Min, ProtocolVersion.Max, config.ProtocolVersion,
                    config.DelegateProtocolMin, config.DelegateProtocolMax, config.DelegateProtocolVersion
                }
            };
            
            stateHandler.HandleAliveNode(alive, false, null);
            
            m._nodeMap.TryGetValue("zero-inc", out var state).Should().BeTrue();
            state!.Incarnation.Should().Be(0);
        }
        finally
        {
            await m.ShutdownAsync();
        }
    }

    [Fact]
    public async Task InvalidPort_Zero_Handled()
    {
        var network = CreateMockNetwork();
        var config = CreateTestConfig("node1");
        config.Transport = network.CreateTransport("node1");
        
        var m = await NSerf.Memberlist.Memberlist.CreateAsync(config);

        try
        {
            var stateHandler = new StateHandlers(m, config.Logger);
            
            var alive = new Alive
            {
                Node = "zero-port-node",
                Addr = IPAddress.Parse("10.0.0.1").GetAddressBytes(),
                Port = 0,
                Incarnation = 1,
                Meta = Array.Empty<byte>(),
                Vsn = new byte[] { 
                    ProtocolVersion.Min, ProtocolVersion.Max, config.ProtocolVersion,
                    config.DelegateProtocolMin, config.DelegateProtocolMax, config.DelegateProtocolVersion
                }
            };
            
            stateHandler.HandleAliveNode(alive, false, null);
            
            m._nodeMap.ContainsKey("zero-port-node").Should().BeTrue();
        }
        finally
        {
            await m.ShutdownAsync();
        }
    }

    [Fact]
    public async Task LargeMetadata_HandlesCorrectly()
    {
        var network = CreateMockNetwork();
        var config = CreateTestConfig("node1");
        config.Transport = network.CreateTransport("node1");
        
        var m = await NSerf.Memberlist.Memberlist.CreateAsync(config);

        try
        {
            var stateHandler = new StateHandlers(m, config.Logger);
            var largeMeta = new byte[512];
            for (int i = 0; i < 512; i++) largeMeta[i] = (byte)(i % 256);
            
            var alive = new Alive
            {
                Node = "large-meta-node",
                Addr = IPAddress.Parse("10.0.0.1").GetAddressBytes(),
                Port = 7946,
                Incarnation = 1,
                Meta = largeMeta,
                Vsn = new byte[] { 
                    ProtocolVersion.Min, ProtocolVersion.Max, config.ProtocolVersion,
                    config.DelegateProtocolMin, config.DelegateProtocolMax, config.DelegateProtocolVersion
                }
            };
            
            stateHandler.HandleAliveNode(alive, false, null);
            
            m._nodeMap["large-meta-node"].Node.Meta.Should().HaveCount(512);
        }
        finally
        {
            await m.ShutdownAsync();
        }
    }

    [Fact]
    public async Task RapidStateChanges_MaintainsConsistency()
    {
        var network = CreateMockNetwork();
        var config = CreateTestConfig("node1");
        config.Transport = network.CreateTransport("node1");
        config.SuspicionMult = 100;
        
        var m = await NSerf.Memberlist.Memberlist.CreateAsync(config);

        try
        {
            var stateHandler = new StateHandlers(m, config.Logger);
            
            for (int i = 0; i < 50; i++)
            {
                var alive = new Alive
                {
                    Node = "rapid-node",
                    Addr = IPAddress.Parse("10.0.0.1").GetAddressBytes(),
                    Port = 7946,
                    Incarnation = (uint)i,
                    Meta = Array.Empty<byte>(),
                    Vsn = new byte[] { 
                        ProtocolVersion.Min, ProtocolVersion.Max, config.ProtocolVersion,
                        config.DelegateProtocolMin, config.DelegateProtocolMax, config.DelegateProtocolVersion
                    }
                };
                stateHandler.HandleAliveNode(alive, false, null);
                
                if (i % 5 == 0)
                {
                    var suspect = new Suspect { Node = "rapid-node", Incarnation = (uint)i, From = "other" };
                    stateHandler.HandleSuspectNode(suspect);
                }
            }
            
            m._nodeMap["rapid-node"].Incarnation.Should().Be(49);
        }
        finally
        {
            await m.ShutdownAsync();
        }
    }

    [Fact]
    public async Task DuplicateNodeNames_HigherIncarnationWins()
    {
        var network = CreateMockNetwork();
        var config = CreateTestConfig("node1");
        config.Transport = network.CreateTransport("node1");
        
        var m = await NSerf.Memberlist.Memberlist.CreateAsync(config);

        try
        {
            var stateHandler = new StateHandlers(m, config.Logger);
            
            var alive1 = new Alive
            {
                Node = "dup-node",
                Addr = IPAddress.Parse("10.0.0.1").GetAddressBytes(),
                Port = 7946,
                Incarnation = 1,
                Meta = new byte[] { 1 },
                Vsn = new byte[] { 
                    ProtocolVersion.Min, ProtocolVersion.Max, config.ProtocolVersion,
                    config.DelegateProtocolMin, config.DelegateProtocolMax, config.DelegateProtocolVersion
                }
            };
            stateHandler.HandleAliveNode(alive1, false, null);
            
            var alive2 = new Alive
            {
                Node = "dup-node",
                Addr = IPAddress.Parse("10.0.0.1").GetAddressBytes(),
                Port = 7946,
                Incarnation = 5,
                Meta = new byte[] { 2 },
                Vsn = new byte[] { 
                    ProtocolVersion.Min, ProtocolVersion.Max, config.ProtocolVersion,
                    config.DelegateProtocolMin, config.DelegateProtocolMax, config.DelegateProtocolVersion
                }
            };
            stateHandler.HandleAliveNode(alive2, false, null);
            
            m._nodeMap["dup-node"].Incarnation.Should().Be(5);
            m._nodeMap["dup-node"].Node.Meta.Should().BeEquivalentTo(new byte[] { 2 });
        }
        finally
        {
            await m.ShutdownAsync();
        }
    }
}

// Ported from: github.com/hashicorp/memberlist/state_test.go (conflict scenarios)
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

public class ConflictTests
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
    public async Task Conflict_DifferentAddress_HandledCorrectly()
    {
        var network = CreateMockNetwork();
        var config = CreateTestConfig("node1");
        config.Transport = network.CreateTransport("node1");
        
        var m = await NSerf.Memberlist.Memberlist.CreateAsync(config);

        try
        {
            var stateHandler = new StateHandlers(m, config.Logger);
            m.NextIncarnation();
            var initialInc = m.Incarnation;

            var conflictAlive = new Alive
            {
                Node = "node1",
                Addr = IPAddress.Parse("192.168.99.99").GetAddressBytes(),
                Port = 9999,
                Incarnation = initialInc,
                Meta = Array.Empty<byte>(),
                Vsn = new byte[] { 
                    ProtocolVersion.Min, ProtocolVersion.Max, config.ProtocolVersion,
                    config.DelegateProtocolMin, config.DelegateProtocolMax, config.DelegateProtocolVersion
                }
            };

            stateHandler.HandleAliveNode(conflictAlive, false, null);

            m._nodeMap["node1"].State.Should().Be(NodeStateType.Alive);
        }
        finally
        {
            await m.ShutdownAsync();
        }
    }

    [Fact]
    public async Task Conflict_SuspectMessage_RefutesCorrectly()
    {
        var network = CreateMockNetwork();
        var config = CreateTestConfig("localnode");
        config.Transport = network.CreateTransport("localnode");
        
        var m = await NSerf.Memberlist.Memberlist.CreateAsync(config);

        try
        {
            var stateHandler = new StateHandlers(m, config.Logger);
            m.NextIncarnation();
            var currentInc = m.Incarnation;

            var conflictSuspect = new Suspect
            {
                Node = "localnode",
                Incarnation = currentInc,
                From = "othernode"
            };

            stateHandler.HandleSuspectNode(conflictSuspect);

            m.Incarnation.Should().BeGreaterThan(currentInc);
            m._nodeMap["localnode"].State.Should().Be(NodeStateType.Alive);
        }
        finally
        {
            await m.ShutdownAsync();
        }
    }

    [Fact]
    public async Task Conflict_DeadMessage_RefutesCorrectly()
    {
        var network = CreateMockNetwork();
        var config = CreateTestConfig("survivor");
        config.Transport = network.CreateTransport("survivor");
        
        var m = await NSerf.Memberlist.Memberlist.CreateAsync(config);

        try
        {
            var stateHandler = new StateHandlers(m, config.Logger);
            m.NextIncarnation();
            var initialInc = m.Incarnation;

            var falseDeath = new Dead
            {
                Node = "survivor",
                Incarnation = initialInc,
                From = "falsereporter"
            };

            stateHandler.HandleDeadNode(falseDeath);

            m.Incarnation.Should().BeGreaterThan(initialInc);
            m._nodeMap["survivor"].State.Should().Be(NodeStateType.Alive);
        }
        finally
        {
            await m.ShutdownAsync();
        }
    }

    [Fact]
    public async Task Conflict_HigherIncarnationDead_RefutesCorrectly()
    {
        var network = CreateMockNetwork();
        var config = CreateTestConfig("node1");
        config.Transport = network.CreateTransport("node1");
        
        var m = await NSerf.Memberlist.Memberlist.CreateAsync(config);

        try
        {
            var stateHandler = new StateHandlers(m, config.Logger);
            m.NextIncarnation();
            var initialInc = m.Incarnation;

            var higherDead = new Dead
            {
                Node = "node1",
                Incarnation = initialInc + 5,
                From = "accuser"
            };

            stateHandler.HandleDeadNode(higherDead);

            m.Incarnation.Should().BeGreaterThan(initialInc + 5);
            m._nodeMap["node1"].State.Should().Be(NodeStateType.Alive);
        }
        finally
        {
            await m.ShutdownAsync();
        }
    }

    [Fact]
    public async Task Conflict_LowerIncarnationAboutOther_Ignored()
    {
        var network = CreateMockNetwork();
        var config = CreateTestConfig("testnode");
        config.Transport = network.CreateTransport("testnode");
        
        var m = await NSerf.Memberlist.Memberlist.CreateAsync(config);

        try
        {
            var stateHandler = new StateHandlers(m, config.Logger);
            
            var alive = new Alive
            {
                Node = "othernode",
                Addr = IPAddress.Parse("10.10.10.10").GetAddressBytes(),
                Port = 7946,
                Incarnation = 10,
                Meta = Array.Empty<byte>(),
                Vsn = new byte[] { 
                    ProtocolVersion.Min, ProtocolVersion.Max, config.ProtocolVersion,
                    config.DelegateProtocolMin, config.DelegateProtocolMax, config.DelegateProtocolVersion
                }
            };
            stateHandler.HandleAliveNode(alive, false, null);

            var oldSuspect = new Suspect
            {
                Node = "othernode",
                Incarnation = 5,
                From = "attacker"
            };

            stateHandler.HandleSuspectNode(oldSuspect);

            m._nodeMap["othernode"].State.Should().Be(NodeStateType.Alive);
            m._nodeMap["othernode"].Incarnation.Should().Be(10);
        }
        finally
        {
            await m.ShutdownAsync();
        }
    }

    [Fact]
    public async Task Refutation_MultipleAttacks_DefendsCorrectly()
    {
        var network = CreateMockNetwork();
        var config = CreateTestConfig("defender");
        config.Transport = network.CreateTransport("defender");
        
        var m = await NSerf.Memberlist.Memberlist.CreateAsync(config);

        try
        {
            var stateHandler = new StateHandlers(m, config.Logger);
            m.NextIncarnation();
            var startInc = m.Incarnation;

            for (int i = 0; i < 10; i++)
            {
                var attack = new Suspect
                {
                    Node = "defender",
                    Incarnation = m.Incarnation,
                    From = $"attacker{i}"
                };
                stateHandler.HandleSuspectNode(attack);
            }

            m.Incarnation.Should().BeGreaterThan(startInc);
            m._nodeMap["defender"].State.Should().Be(NodeStateType.Alive);
        }
        finally
        {
            await m.ShutdownAsync();
        }
    }
}

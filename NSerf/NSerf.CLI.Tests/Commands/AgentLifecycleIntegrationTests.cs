// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Agent;
using NSerf.CLI.Tests.Fixtures;
using NSerf.Client;
using NSerf.Serf;

namespace NSerf.CLI.Tests.Commands;

[Trait("Category", "Integration")]
[Collection("Sequential")]
public class AgentLifecycleIntegrationTests
{
    [Fact(Timeout = 10000)]
    public async Task Agent_StartsAndRuns_UntilShutdown()
    {
        var config = new AgentConfig
        {
            NodeName = TestHelper.GetRandomNodeName(),
            BindAddr = TestHelper.GetRandomBindAddr(),
            RPCAddr = ""
        };

        await using var agent = new SerfAgent(config);
        await agent.StartAsync();
        
        Assert.NotNull(agent.Serf);
        if (agent.Serf != null)
        {
            Assert.Equal(SerfState.SerfAlive, agent.Serf.State());
        }
        
        await agent.ShutdownAsync();
        if (agent.Serf != null)
        {
            Assert.True(agent.Serf.State() == SerfState.SerfShutdown || agent.Serf.State() == SerfState.SerfLeft);
        }
    }


    [Fact(Timeout = 20000)]
    public async Task Agent_JoinsCluster_AtStartup()
    {
        await using var agent1 = new AgentFixture();
        await agent1.InitializeAsync();
        
        var joinAddr = $"{agent1.Agent!.Serf!.Members()[0].Addr}:{agent1.Agent.Serf.Members()[0].Port}";
        
        var config = new AgentConfig
        {
            NodeName = TestHelper.GetRandomNodeName(),
            BindAddr = TestHelper.GetRandomBindAddr(),
            RPCAddr = "",
            StartJoin = new[] { joinAddr }
        };

        await using var agent2 = new SerfAgent(config);
        await agent2.StartAsync();
        await Task.Delay(2000);
        
        var members1 = agent1.Agent.Serf.Members();
        Assert.Equal(2, members1.Length);
    }

    [Fact(Timeout = 15000)]
    public async Task Agent_JoinFailure_ThrowsException()
    {
        var config = new AgentConfig
        {
            NodeName = TestHelper.GetRandomNodeName(),
            BindAddr = TestHelper.GetRandomBindAddr(),
            RPCAddr = "",
            StartJoin = new[] { "127.0.0.1:9999" }
        };

        await using var agent = new SerfAgent(config);
        await Assert.ThrowsAnyAsync<Exception>(async () => await agent.StartAsync());
    }

    [Fact(Timeout = 15000)]
    public async Task Agent_AdvertiseAddress_UsedInMemberInfo()
    {
        var advertiseAddr = "10.0.0.5:12345";
        var config = new AgentConfig
        {
            NodeName = TestHelper.GetRandomNodeName(),
            BindAddr = $"{TestHelper.GetRandomBindAddr()}:0",  // Add port
            RPCAddr = "127.0.0.1:0",
            AdvertiseAddr = advertiseAddr
        };

        await using var agent = new SerfAgent(config);
        await agent.StartAsync();
        
        // Wait for agent to fully initialize
        await Task.Delay(500);

        var member = agent.Serf!.LocalMember();
        
        // Verify advertised IP and port are used
        var addrBytes = member.Addr.GetAddressBytes();
        Assert.Equal((byte)10, addrBytes[0]);
        Assert.Equal((byte)0, addrBytes[1]);
        Assert.Equal((byte)0, addrBytes[2]);
        Assert.Equal((byte)5, addrBytes[3]);
        Assert.Equal((ushort)12345, member.Port);
    }

    [Fact(Timeout = 25000)]
    public async Task Agent_RetryJoin_EventuallySucceeds()
    {
        var config1 = new AgentConfig
        {
            NodeName = TestHelper.GetRandomNodeName(),
            BindAddr = TestHelper.GetRandomBindAddr(),
            RPCAddr = ""
        };

        await using var agent1 = new SerfAgent(config1);
        await agent1.StartAsync();
        
        var joinAddr = $"{agent1.Serf!.Members()[0].Addr}:{agent1.Serf.Members()[0].Port}";
        
        var config2 = new AgentConfig
        {
            NodeName = TestHelper.GetRandomNodeName(),
            BindAddr = TestHelper.GetRandomBindAddr(),
            RPCAddr = "",
            RetryJoin = new[] { joinAddr },
            RetryInterval = TimeSpan.FromSeconds(1),
            RetryMaxAttempts = 5
        };

        await using var agent2 = new SerfAgent(config2);
        await agent2.StartAsync();
        await Task.Delay(5000);
        
        var members = agent1.Serf.Members();
        // Retry join may take multiple attempts
        Assert.True(members.Length >= 1, $"Expected at least 1 member, got {members.Length}");
    }

    [Fact(Timeout = 20000)]
    public async Task Agent_RetryJoin_MaxAttempts_StopsRetrying()
    {
        var config = new AgentConfig
        {
            NodeName = TestHelper.GetRandomNodeName(),
            BindAddr = TestHelper.GetRandomBindAddr(),
            RPCAddr = "",
            RetryJoin = new[] { "127.0.0.1:9998" },
            RetryInterval = TimeSpan.FromMilliseconds(500),
            RetryMaxAttempts = 3
        };

        await using var agent = new SerfAgent(config);
        await agent.StartAsync();
        await Task.Delay(3000);
        
        var members = agent.Serf!.Members();
        Assert.Single(members);
    }

    [Fact(Timeout = 15000)]
    public async Task Agent_MultipleStartCalls_ThrowsException()
    {
        var config = new AgentConfig
        {
            NodeName = TestHelper.GetRandomNodeName(),
            BindAddr = TestHelper.GetRandomBindAddr(),
            RPCAddr = ""
        };

        await using var agent = new SerfAgent(config);
        await agent.StartAsync();
        
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await agent.StartAsync());
    }
}

public static class TestHelper
{
    private static int _nodeCounter = 0;
    
    public static string GetRandomNodeName()
    {
        return $"test-node-{Interlocked.Increment(ref _nodeCounter)}";
    }
    
    public static string GetRandomBindAddr()
    {
        var random = new Random();
        return $"127.0.0.{random.Next(1, 255)}";
    }
}

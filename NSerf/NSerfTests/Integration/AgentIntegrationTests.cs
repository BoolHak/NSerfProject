// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Agent;
using NSerf.Client;
using Xunit;

namespace NSerfTests.Integration;

public class AgentIntegrationTests
{
    [Fact(Timeout = 10000)]
    public async Task Agent_StartAndStop_Success()
    {
        var config = new AgentConfig
        {
            NodeName = "test-node-1",
            BindAddr = "127.0.0.1:0",
            RpcAddr = "127.0.0.1:17373"
        };

        await using var agent = new SerfAgent(config);
        await agent.StartAsync();

        Assert.NotNull(agent.Serf);

        await agent.DisposeAsync();
    }

    [Fact(Timeout = 10000)]
    public async Task Agent_RpcClient_CanConnect()
    {
        var config = new AgentConfig
        {
            NodeName = "test-node-2",
            BindAddr = "127.0.0.1:0",
            RpcAddr = "127.0.0.1:17374"
        };

        await using var agent = new SerfAgent(config);
        await agent.StartAsync();
        await Task.Delay(500);

        var rpcConfig = new RpcConfig
        {
            Address = "127.0.0.1:17374",
            Timeout = TimeSpan.FromSeconds(5)
        };

        using var client = new RpcClient(rpcConfig);
        await client.ConnectAsync();

        var members = await client.MembersAsync();
        Assert.NotNull(members);
        Assert.Single(members);
        Assert.Equal("test-node-2", members[0].Name);

        await agent.DisposeAsync();
    }

    [Fact(Timeout = 15000)]
    public async Task Agent_RpcClient_JoinCommand()
    {
        var config1 = new AgentConfig
        {
            NodeName = "node1",
            BindAddr = "127.0.0.1:17946",
            RpcAddr = "127.0.0.1:17375"
        };

        var config2 = new AgentConfig
        {
            NodeName = "node2",
            BindAddr = "127.0.0.1:17947",
            RpcAddr = "127.0.0.1:17376"
        };

        await using var agent1 = new SerfAgent(config1);
        await using var agent2 = new SerfAgent(config2);
        
        await agent1.StartAsync();
        await agent2.StartAsync();

        var rpcConfig = new RpcConfig
        {
            Address = "127.0.0.1:17376",
            Timeout = TimeSpan.FromSeconds(5)
        };

        using var client = new RpcClient(rpcConfig);
        await client.ConnectAsync();

        var joined = await client.JoinAsync(new[] { "127.0.0.1:17946" }, false);
        Assert.Equal(1, joined);

        await Task.Delay(500);

        var members = await client.MembersAsync();
        Assert.Equal(2, members.Length);

        await agent2.DisposeAsync();
        await agent1.DisposeAsync();
    }

    [Fact(Timeout = 10000)]
    public async Task Agent_RpcClient_LeaveCommand()
    {
        var config = new AgentConfig
        {
            NodeName = "test-leave",
            BindAddr = "127.0.0.1:0",
            RpcAddr = "127.0.0.1:17377"
        };

        await using var agent = new SerfAgent(config);
        await agent.StartAsync();

        var rpcConfig = new RpcConfig
        {
            Address = "127.0.0.1:17377",
            Timeout = TimeSpan.FromSeconds(5)
        };

        using var client = new RpcClient(rpcConfig);
        await client.ConnectAsync();

        await client.LeaveAsync();

        await Task.Delay(200);

        await agent.DisposeAsync();
    }

    [Fact(Timeout = 10000)]
    public async Task Agent_RpcClient_UserEvent()
    {
        var config = new AgentConfig
        {
            NodeName = "event-node",
            BindAddr = "127.0.0.1:0",
            RpcAddr = "127.0.0.1:17378"
        };

        await using var agent = new SerfAgent(config);
        await agent.StartAsync();

        var rpcConfig = new RpcConfig
        {
            Address = "127.0.0.1:17378",
            Timeout = TimeSpan.FromSeconds(5)
        };

        using var client = new RpcClient(rpcConfig);
        await client.ConnectAsync();

        var payload = System.Text.Encoding.UTF8.GetBytes("test payload");
        await client.UserEventAsync("test-event", payload, false);

        await agent.DisposeAsync();
    }

    [Fact(Timeout = 10000)]
    public async Task Agent_RpcClient_Stats()
    {
        var config = new AgentConfig
        {
            NodeName = "stats-node",
            BindAddr = "127.0.0.1:0",
            RpcAddr = "127.0.0.1:17379"
        };

        await using var agent = new SerfAgent(config);
        await agent.StartAsync();

        var rpcConfig = new RpcConfig
        {
            Address = "127.0.0.1:17379",
            Timeout = TimeSpan.FromSeconds(5)
        };

        using var client = new RpcClient(rpcConfig);
        await client.ConnectAsync();

        var stats = await client.StatsAsync();
        Assert.NotNull(stats);
        Assert.NotEmpty(stats);

        await agent.DisposeAsync();
    }
}

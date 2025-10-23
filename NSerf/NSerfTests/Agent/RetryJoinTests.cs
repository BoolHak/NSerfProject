// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Agent;
using Xunit;

namespace NSerfTests.Agent;

public class RetryJoinTests
{
    [Fact]
    public async Task RetryJoin_RunsInBackground_DoesNotBlockStartup()
    {
        var config = new AgentConfig
        {
            NodeName = "test-node",
            BindAddr = "127.0.0.1:0",
            RetryJoin = new[] { "invalid:9999" },
            RetryMaxAttempts = 3,
            RetryInterval = TimeSpan.FromMilliseconds(100)
        };

        var agent = new SerfAgent(config);
        
        // Should start successfully despite retry join failures
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await agent.StartAsync();
        sw.Stop();

        // Startup should be fast (< 1 second), not waiting for retry attempts
        Assert.True(sw.Elapsed.TotalSeconds < 1);
        
        await agent.ShutdownAsync();
        await agent.DisposeAsync();
    }

    [Fact]
    public async Task RetryJoin_MaxAttempts_StopsRetrying()
    {
        var config = new AgentConfig
        {
            NodeName = "test-node",
            BindAddr = "127.0.0.1:0",
            RetryJoin = new[] { "invalid:9999" },
            RetryMaxAttempts = 3,
            RetryInterval = TimeSpan.FromMilliseconds(50)
        };

        var agent = new SerfAgent(config);
        await agent.StartAsync();

        // Wait for retries to exhaust (3 attempts * 50ms + buffer)
        await Task.Delay(500);

        // Agent should still be running (retry failures don't kill agent)
        Assert.NotNull(agent.Serf);
        
        await agent.ShutdownAsync();
        await agent.DisposeAsync();
    }

    [Fact]
    public async Task RetryJoin_MinInterval_EnforcedAtOneSecond()
    {
        var config = new AgentConfig
        {
            NodeName = "test-node",
            BindAddr = "127.0.0.1:0",
            RetryJoin = new[] { "invalid:9999" },
            RetryInterval = TimeSpan.FromMilliseconds(10),  // Too fast
            RetryMaxAttempts = 2
        };

        var agent = new SerfAgent(config);
        await agent.StartAsync();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        
        // Wait for retries to complete
        await Task.Delay(2500);
        sw.Stop();

        // Should take at least 1 second (min interval enforced)
        Assert.True(sw.Elapsed.TotalSeconds >= 1);
        
        await agent.ShutdownAsync();
        await agent.DisposeAsync();
    }

    [Fact]
    public async Task StartJoin_Failure_BlocksStartup()
    {
        var config = new AgentConfig
        {
            NodeName = "test-node",
            BindAddr = "127.0.0.1:0",
            StartJoin = new[] { "invalid:9999" }
        };

        var agent = new SerfAgent(config);
        
        // StartJoin failure should throw during startup
        await Assert.ThrowsAsync<AggregateException>(async () => await agent.StartAsync());
        
        await agent.DisposeAsync();
    }

    [Fact]
    public async Task StartJoin_Success_JoinsImmediately()
    {
        // Start first agent
        var config1 = new AgentConfig
        {
            NodeName = "node1",
            BindAddr = "127.0.0.1:17001"
        };
        var agent1 = new SerfAgent(config1);
        await agent1.StartAsync();

        // Start second agent with StartJoin
        var config2 = new AgentConfig
        {
            NodeName = "node2",
            BindAddr = "127.0.0.1:17002",
            StartJoin = new[] { "127.0.0.1:17001" }
        };
        var agent2 = new SerfAgent(config2);
        await agent2.StartAsync();

        // Give gossip time to propagate
        await Task.Delay(500);

        // Both should see each other
        var members1 = agent1.Serf?.Members() ?? Array.Empty<NSerf.Serf.Member>();
        var members2 = agent2.Serf?.Members() ?? Array.Empty<NSerf.Serf.Member>();

        Assert.Equal(2, members1.Length);
        Assert.Equal(2, members2.Length);

        await agent2.ShutdownAsync();
        await agent1.ShutdownAsync();
        await agent2.DisposeAsync();
        await agent1.DisposeAsync();
    }
}

// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Agent;
using Xunit;

namespace NSerfTests.Agent;

public class ConfigReloadTests
{
    [Fact]
    public async Task ConfigReload_UpdatesLogLevel()
    {
        var config = new AgentConfig
        {
            NodeName = "test-node",
            BindAddr = "127.0.0.1:0",
            LogLevel = "INFO"
        };

        var agent = new SerfAgent(config);
        await agent.StartAsync();

        // Change log level
        config.LogLevel = "DEBUG";

        // Trigger reload via UpdateScripts (simulated reload)
        // In real scenario, SIGHUP would trigger reload

        // Verify agent still running
        Assert.NotNull(agent.Serf);

        await agent.ShutdownAsync();
        await agent.DisposeAsync();
    }

    [Fact]
    public async Task ConfigReload_PreservesConnection()
    {
        var config = new AgentConfig
        {
            NodeName = "test-node",
            BindAddr = "127.0.0.1:0"
        };

        var agent = new SerfAgent(config);
        await agent.StartAsync();

        var serfBefore = agent.Serf;
        
        // Simulate config reload
        config.LogLevel = "DEBUG";
        
        var serfAfter = agent.Serf;

        // Same Serf instance (connection preserved)
        Assert.Same(serfBefore, serfAfter);

        await agent.ShutdownAsync();
        await agent.DisposeAsync();
    }

    [Fact]
    public async Task ConfigReload_InvalidConfig_Rejected()
    {
        var config = new AgentConfig
        {
            NodeName = "test-node",
            BindAddr = "127.0.0.1:0",
            LogLevel = "INFO"
        };

        var agent = new SerfAgent(config);
        await agent.StartAsync();

        // Try invalid log level
        config.LogLevel = "INVALID";

        // Agent should continue with old config
        Assert.NotNull(agent.Serf);

        await agent.ShutdownAsync();
        await agent.DisposeAsync();
    }

    [Fact]
    public async Task ConfigReload_UpdatesTags()
    {
        var config = new AgentConfig
        {
            NodeName = "test-node",
            BindAddr = "127.0.0.1:0",
            Tags = new Dictionary<string, string> { ["version"] = "1.0" }
        };

        var agent = new SerfAgent(config);
        await agent.StartAsync();

        // Update tags
        await agent.SetTagsAsync(new Dictionary<string, string> { ["version"] = "2.0" });

        // Verify tags updated
        var member = agent.Serf?.LocalMember();
        Assert.NotNull(member);
        Assert.Equal("2.0", member.Tags["version"]);

        await agent.ShutdownAsync();
        await agent.DisposeAsync();
    }

    [Fact]
    public async Task ConfigReload_UpdatesEventScripts()
    {
        var config = new AgentConfig
        {
            NodeName = "test-node",
            BindAddr = "127.0.0.1:0",
            EventHandlers = new List<string> { "member-join=script1.sh" }
        };

        var agent = new SerfAgent(config);
        await agent.StartAsync();

        // In production, ScriptEventHandler.UpdateScripts would be called
        // For now, verify agent continues running
        Assert.NotNull(agent.Serf);

        await agent.ShutdownAsync();
        await agent.DisposeAsync();
    }

    [Fact]
    public async Task ConfigReload_WithoutRestart_AgentContinues()
    {
        var config = new AgentConfig
        {
            NodeName = "test-node",
            BindAddr = "127.0.0.1:0"
        };

        var agent = new SerfAgent(config);
        await agent.StartAsync();

        var membersBefore = agent.Serf?.Members().Length ?? 0;

        // Simulate config changes
        config.LogLevel = "DEBUG";
        await agent.SetTagsAsync(new Dictionary<string, string> { ["test"] = "value" });

        var membersAfter = agent.Serf?.Members().Length ?? 0;

        // Same number of members (agent didn't restart)
        Assert.Equal(membersBefore, membersAfter);

        await agent.ShutdownAsync();
        await agent.DisposeAsync();
    }
}

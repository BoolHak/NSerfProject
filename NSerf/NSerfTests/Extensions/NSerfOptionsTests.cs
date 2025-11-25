using NSerf.Agent;
using NSerf.Extensions;

namespace NSerfTests.Extensions;

public class NSerfOptionsTests
{
    [Fact]
    public void UseCompression_DefaultValue_ShouldBeFalse()
    {
        var options = new NSerfOptions();

        Assert.False(options.UseCompression);
    }

    [Fact]
    public void UseCompression_WhenSetToTrue_ShouldReturnTrue()
    {
        var options = new NSerfOptions
        {
            UseCompression = true
        };

        Assert.True(options.UseCompression);
    }

    [Fact]
    public void ToAgentConfig_WithUseCompressionFalse_ShouldMapToEnableCompressionFalse()
    {
        var options = new NSerfOptions
        {
            UseCompression = false
        };

        var agentConfig = options.ToAgentConfig();

        Assert.False(agentConfig.EnableCompression);
    }

    [Fact]
    public void ToAgentConfig_WithUseCompressionTrue_ShouldMapToEnableCompressionTrue()
    {
        var options = new NSerfOptions
        {
            UseCompression = true
        };

        var agentConfig = options.ToAgentConfig();

        Assert.True(agentConfig.EnableCompression);
    }

    [Fact]
    public void ToAgentConfig_ShouldPreserveOtherPropertiesWhenCompressionSet()
    {
        var options = new NSerfOptions
        {
            NodeName = "test-node",
            UseCompression = true,
            Profile = "wan"
        };

        var agentConfig = options.ToAgentConfig();

        Assert.Equal("test-node", agentConfig.NodeName);
        Assert.True(agentConfig.EnableCompression);
        Assert.Equal("wan", agentConfig.Profile);
    }

    [Fact]
    public async Task SerfAgent_WithCompressionEnabled_ShouldConfigureMemberlistCompression()
    {
        var agentConfig = new AgentConfig
        {
            NodeName = "compression-test-node",
            BindAddr = "127.0.0.1:0",
            EnableCompression = true
        };

        await using var agent = new SerfAgent(agentConfig);
        await agent.StartAsync();

        Assert.NotNull(agent.Serf);
        Assert.NotNull(agent.Serf.Memberlist);
        Assert.True(agent.Serf.Memberlist.Config.EnableCompression);

        await agent.ShutdownAsync();
    }

    [Fact]
    public async Task SerfAgent_WithCompressionDisabled_ShouldConfigureMemberlistWithoutCompression()
    {
        var agentConfig = new AgentConfig
        {
            NodeName = "no-compression-test-node",
            BindAddr = "127.0.0.1:0",
            EnableCompression = false
        };

        await using var agent = new SerfAgent(agentConfig);
        await agent.StartAsync();

        Assert.NotNull(agent.Serf);
        Assert.NotNull(agent.Serf.Memberlist);
        Assert.False(agent.Serf.Memberlist.Config.EnableCompression);

        await agent.ShutdownAsync();
    }
}

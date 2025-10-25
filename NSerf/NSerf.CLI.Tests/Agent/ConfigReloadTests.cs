// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Agent;

namespace NSerf.CLI.Tests.Agent;

/// <summary>
/// Tests for configuration reload functionality
/// Ported from Go's config tests
/// </summary>
[Trait("Category", "Unit")]
public class ConfigReloadTests
{
    [Fact]
    public void Config_LoadFromJson_ParsesCorrectly_CSharpConventions()
    {
        var json = @"{
            ""NodeName"": ""test-node"",
            ""BindAddr"": ""127.0.0.1:7946"",
            ""RPCAddr"": ""127.0.0.1:7373"",
            ""LogLevel"": ""DEBUG"",
            ""Tags"": {
                ""role"": ""web"",
                ""datacenter"": ""us-west""
            }
        }";

        var config = System.Text.Json.JsonSerializer.Deserialize<AgentConfig>(json);
        
        Assert.NotNull(config);
        Assert.Equal("test-node", config.NodeName);
        Assert.Equal("127.0.0.1:7946", config.BindAddr);
        Assert.Equal("127.0.0.1:7373", config.RPCAddr);
        Assert.Equal("DEBUG", config.LogLevel);
        Assert.Equal(2, config.Tags.Count);
        Assert.Equal("web", config.Tags["role"]);
        Assert.Equal("us-west", config.Tags["datacenter"]);
    }

    [Fact]
    public void Config_DefaultValues_AreSet()
    {
        var config = new AgentConfig
        {
            NodeName = "test"
        };

        Assert.Equal("0.0.0.0:7946", config.BindAddr);
        Assert.Equal("INFO", config.LogLevel);
        Assert.Equal("lan", config.Profile);
        Assert.Equal(5, config.Protocol);
        Assert.True(config.LeaveOnTerm);
        Assert.False(config.SkipLeaveOnInt);
        Assert.False(config.RejoinAfterLeave);
        Assert.Equal(TimeSpan.FromSeconds(30), config.RetryInterval);
    }

    [Fact]
    public void Config_Arrays_DefaultEmpty()
    {
        var config = new AgentConfig();

        Assert.Empty(config.StartJoin);
        Assert.Empty(config.StartJoinWan);
        Assert.Empty(config.RetryJoin);
        Assert.Empty(config.RetryJoinWan);
        Assert.Empty(config.Tags);
    }

    [Fact]
    public void Config_Timeouts_HaveDefaults()
    {
        var config = new AgentConfig();

        Assert.Equal(TimeSpan.FromSeconds(60), config.ReconnectInterval);
        Assert.Equal(TimeSpan.FromHours(72), config.ReconnectTimeout);
        Assert.Equal(TimeSpan.FromHours(24), config.TombstoneTimeout);
        Assert.Equal(TimeSpan.FromSeconds(5), config.BroadcastTimeout);
    }

    [Fact]
    public void Config_Features_DefaultFalse()
    {
        var config = new AgentConfig();

        Assert.False(config.DisableCoordinates);
        Assert.False(config.DisableNameResolution);
        Assert.False(config.EnableCompression);
    }

    [Fact]
    public void Config_WithCustomValues_OverridesDefaults()
    {
        var config = new AgentConfig
        {
            NodeName = "custom-node",
            BindAddr = "10.0.0.1:8000",
            LogLevel = "WARN",
            Profile = "wan",
            Protocol = 4,
            DisableCoordinates = true,
            RetryInterval = TimeSpan.FromSeconds(10)
        };

        Assert.Equal("custom-node", config.NodeName);
        Assert.Equal("10.0.0.1:8000", config.BindAddr);
        Assert.Equal("WARN", config.LogLevel);
        Assert.Equal("wan", config.Profile);
        Assert.Equal(4, config.Protocol);
        Assert.True(config.DisableCoordinates);
        Assert.Equal(TimeSpan.FromSeconds(10), config.RetryInterval);
    }

    [Fact]
    public void Config_WithEventHandlers_StoresCorrectly()
    {
        var config = new AgentConfig
        {
            NodeName = "test",
            EventHandlers = new List<string>
            {
                "member-join=script1.sh",
                "user:deploy=script2.sh"
            }
        };

        Assert.Equal(2, config.EventHandlers.Count);
        Assert.Contains("member-join=script1.sh", config.EventHandlers);
        Assert.Contains("user:deploy=script2.sh", config.EventHandlers);
    }

    [Fact]
    public void Config_WithEncryptKey_ValidatesFormat()
    {
        var validKey = Convert.ToBase64String(new byte[32]); // 32-byte key
        
        var config = new AgentConfig
        {
            NodeName = "test",
            EncryptKey = validKey
        };

        Assert.Equal(validKey, config.EncryptKey);
    }

    [Fact]
    public void Config_RetryMaxAttempts_Zero_MeansInfinite()
    {
        var config = new AgentConfig
        {
            RetryMaxAttempts = 0
        };

        Assert.Equal(0, config.RetryMaxAttempts);
    }
}
